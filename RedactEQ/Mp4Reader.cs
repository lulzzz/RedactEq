using Equature.Integration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VideoTools
{
    public struct ProgressStruct
    {
        public double timestamp;  // current frame position within video (milliseconds)
        public long length;    // total length of video (milliseconds)

        public byte[] data;
        public bool key;
        public bool finished;
        public int frameIndex;
        public int width;
        public int height;
        public List<DNNTools.BoundingBox> boxList;
        public ProgressStruct(double tstamp, long leng, byte[] imageData, int FrameIndex, bool isKeyFrame, bool isFinished,  int w, int h, List<DNNTools.BoundingBox> BoxList = null)
        {
            timestamp = tstamp;
            length = leng;
            data = imageData;
            frameIndex = FrameIndex;
            width = w;
            height = h;
            key = isKeyFrame;
            finished = isFinished;
            boxList = BoxList;
        }
    }

    public class Mp4Reader
    {
        private string m_errorMsg;
        int m_frameCount;




        public async void StartPlayback(string filename, Action<ProgressStruct> newFrameHandler,
            int decodeWidth, int decodeHeight, double startTimestamp, double endTimestamp, DNNTools.DNNengine dnnEngine, float confidence, bool useTracker,
            CancellationTokenSource tokenSource,
            WPFTools.PauseTokenSource pauseTokenSource,
            bool paceOutput, Dictionary<double, int> frameIndexLookup)
                {
                    //construct Progress<T>, passing ReportProgress as the Action<T> 
                    var progressIndicator = new Progress<ProgressStruct>(newFrameHandler);

                    //call async method
                    long position = await PlayMp4FileAsync(filename, decodeWidth, decodeHeight, startTimestamp, endTimestamp, dnnEngine, confidence, useTracker,
                        progressIndicator, tokenSource.Token, pauseTokenSource.Token, paceOutput, frameIndexLookup);
                }



        async Task<long> PlayMp4FileAsync(string path, int targetWidth, int targetHeight, double startTimestamp, double endTimestamp, 
            DNNTools.DNNengine dnnEngine, float confidence, bool useTracker,
            IProgress<ProgressStruct> progress,
            CancellationToken token, WPFTools.PauseToken pauseToken,
            bool paceOutput, Dictionary<double, int> frameIndexLookup)
        {
            // path - filename/path to Mp4 file
            // startingAt - point in time to start decoding/playback, given in milliseconds
            // targetWidth, targetHeight - desired pixel dimension of decoded frames
            // paceOutput - flag indicating whether to pace the output, using frame 
            //              timestamps and framerate, so that playback is at video rate

            long count = -1;

            if (File.Exists(path))
            {
                count = await Task.Run<long>(async () =>
                {
                    double timestamp = 0.0f;
                    Stopwatch sw = new Stopwatch();
                    IntPtr mp4Reader = IntPtr.Zero;

                    try
                    {
                        mp4Reader = Mp4.CreateMp4Reader(path);
                    }
                    catch(Exception ex)
                    {
                        string message = ex.Message;
                    }

                    DNNTools.NonMaximumSuppression nms = new DNNTools.NonMaximumSuppression();
                    nms.Init();

                    DNNTools.MultiTracker multiTracker = new DNNTools.MultiTracker();
                    

                    if (mp4Reader != IntPtr.Zero)
                    {
                        try
                        {
                            long durationMilliseconds;
                            double frameRate;
                            long timestampDelta;
                            long timestampWindow;
                            int height;
                            int width;
                            int sampleCount;


                            // Get the video metadata from the file
                            if (Mp4.GetVideoProperties(mp4Reader, out durationMilliseconds, out frameRate, out width, out height, out sampleCount))
                            {
                                timestampDelta = (long)(1000.0f / frameRate);
                                timestampWindow = timestampDelta / 2;


                                // Ask the decoder to resample to targetWidth x targetHeight, and assume 24-bit RGB colorspace
                                byte[] frame = new byte[targetWidth * targetHeight * 3];
                                bool key;

                                ProgressStruct prog;

                                m_frameCount = 0;

                                // move to starting position
                                double actualStart = Mp4.SetTimePositionAbsolute(mp4Reader, startTimestamp);

                                int frameIndex = 0;

                                // get the starting frame index
                                int tempIndex = 0;
                                if (frameIndexLookup != null)
                                    if (frameIndexLookup.TryGetValue(actualStart, out tempIndex))
                                    {
                                        frameIndex = tempIndex;
                                    }


                                // create flag used to quit early
                                bool running = true;


                                if (actualStart == -1) // failed to move to start position
                                {
                                    running = false;
                                }

                                sw.Start();
                                

                                while (running)
                                {
                                    timestamp = (double)Mp4.GetNextVideoFrame(mp4Reader, frame, out key, targetWidth, targetHeight) / 1000.0;
                                    
                                    if (timestamp == -0.001)   // EOF                             
                                    {
                                        running = false;
                                        break;
                                    }

                                    if (token.IsCancellationRequested)
                                    {
                                        // pause or stop requested
                                        running = false;
                                        break;
                                    }
                                    

                                    byte[] frameCopy = new byte[targetWidth * targetHeight * 3];
                                    Buffer.BlockCopy(frame, 0, frameCopy, 0, targetWidth * targetHeight * 3);
                                    prog = new ProgressStruct(timestamp, durationMilliseconds, frameCopy, frameIndex, key, !running, targetWidth, targetHeight);

                                    if (dnnEngine != null)
                                    {
                                        prog.boxList = dnnEngine.EvalImage(frameCopy, targetWidth, targetHeight, 3, targetWidth, targetHeight, confidence);

                                        prog.boxList = nms.Execute(prog.boxList, 0.50f);

                                        if (useTracker)
                                        {
                                            List<DNNTools.BoundingBox> trackedBoxes = multiTracker.Update(frameCopy, targetWidth, targetHeight, prog.boxList);

                                            prog.boxList.AddRange(trackedBoxes);

                                            prog.boxList = nms.Execute(prog.boxList, 0.50f);
                                        }
                                        else
                                        {
                                            multiTracker.ClearTrackers();
                                        }
                                    }
                                                                        

                                    if (progress != null && prog.data != null)
                                    {
                                        // if we're in playback mode, pace the frames appropriately
                                        if (paceOutput)
                                        {
                                            while (sw.ElapsedMilliseconds < timestampDelta)
                                            {
                                                Thread.Sleep(1);
                                            }
                                            sw.Restart();
                                        }

                                        m_frameCount++;

                                        // send frame to UI thread
                                        progress.Report(prog);
                                    }

                                    await pauseToken.WaitWhilePausedAsync();

                                    if(timestamp >= endTimestamp)
                                    {
                                        break;
                                    }

                                    frameIndex++;
                                }
                            }

                        }
                        catch (Exception ex)
                        {
                            m_errorMsg = ex.Message;
                            timestamp = -2;  // indicating an exception occurred
                        }
                        finally
                        {
                            try
                            {
                                Mp4.DestroyMp4Reader(mp4Reader);
                            }
                            catch(Exception ex)
                            {
                                string message = ex.Message;
                            }
                            progress.Report(new ProgressStruct(-0.001, 0, null, m_frameCount,false,true , 0, 0)); // signal that the player stopped (timestamp = -0.001)
                        }
                    }
                    else
                    {
                        timestamp = -3;
                        m_errorMsg = "Could not open file.";
                    }

                    return (long)timestamp;

                }, token);

            } // END File.Exists

            return count;
        }



        public string GetLastError()
        {
            return m_errorMsg;
        }


    }
}
