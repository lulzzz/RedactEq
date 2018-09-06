using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using WPFTools;

namespace VideoTools
{
    class FrameRecord
    {
        public double timestamp;  // frame timestamp
        public string filename; // filename in cache
        public FrameRecord(double TimeStamp, string FileName)
        {
            timestamp = TimeStamp;
            filename = FileName;
        }
        ~FrameRecord()
        {
            if (File.Exists(filename))
                File.Delete(filename);
        }
    }

    class GopRecord
    {
        public int frameIndex;    // the frame number for this GOP from all frames in a file 
        public double timestamp; // timestamp of first frame in GOP (i.e. the key frame)                               
        public int numFrames;  // number of frames in the GOP
        public GopRecord(int FrameIndex, double TimeStamp, int NumFramesInGop)
        {
            frameIndex = FrameIndex;
            timestamp = TimeStamp;
            numFrames = NumFramesInGop;
        }
    }
    
    public class VideoCache
    {
        SortedList<int,GopRecord> m_gopList;  // sorted list of all gop timestamps in mp4 file
                                              // key = frame index (within entire movie) for the key frame of this gop
                                              // value = GopRecord containing timestamp of key frame and numFrames in gop

        Dictionary<int,FrameRecord> m_frameCache; // key = frame index within entire movie, value = filename
        int m_padding;
        string m_mp4Filename;
        string m_cacheDirectory;
        BinaryReader m_reader;
        BinaryWriter m_writer;

        int m_currentFrameIndex;
        int m_currentGopIndex;
        int m_currentGop_startFrameIndex;
        int m_currentGop_endFrameIndex;
        int m_frameCache_startFrameIndex;
        int m_frameCache_endFrameIndex;
        int m_frameCache_startGopIndex;
        int m_frameCache_endGopIndex;
        int m_maxFrameIndex;


        // Events
        public delegate void VideoCache_EventHandler(object sender, VideoCache_EventArgs e);
        public event VideoCache_EventHandler VideoCacheEvent;
        protected virtual void OnVideoCacheEvent(VideoCache_EventArgs e)
        {
            if (VideoCacheEvent != null) VideoCacheEvent(this, e);
        }


        public VideoCache(string cacheName, string mp4Filename, int padding)
        {
            m_cacheDirectory = GetCacheDirectory(cacheName);
            m_mp4Filename = mp4Filename;
            m_padding = padding;

            m_gopList = new SortedList<int, GopRecord>();
            m_frameCache = new Dictionary<int, FrameRecord>(); 
        }

        ~VideoCache()
        {
            try
            {
                if (Directory.Exists(m_cacheDirectory))
                    Directory.Delete(m_cacheDirectory);
            }
            catch
            { }
        }

        public bool Init()
        {
            bool success = true;

            success = GetGopList(m_mp4Filename);

            // load the first Gop plus m_padding number of Gops after that
            InitCache();

            return success;
        }

        public string GetCacheDirectory(string cacheName)
        {
            string dir = Environment.CurrentDirectory;
            return Path.Combine(dir, cacheName);
        }

        public string BuildFilenameFromTimestamp(double timestamp)
        {            
            return Path.Combine(m_cacheDirectory, ((int)(timestamp*1000)).ToString("d9") + ".frame");
        }

        public bool GetGopList(string mp4Filename)
        {
            bool success = true;
            m_gopList.Clear();

            // TEMP
            string[] files = Directory.GetFiles("d:/temp1/frames", "*.jpg");

            int gopSize = 10;
            int gopIndex = 0;
            double milliSecondsBetweenFrames = 0.010;
            for(int frameIndex = 0; frameIndex < files.Length; frameIndex++)
            {
                if(frameIndex % gopSize == 0)
                {
                    double timestamp = frameIndex * milliSecondsBetweenFrames;
                    m_gopList.Add(gopIndex, new GopRecord(frameIndex, timestamp, gopSize));

                    gopIndex++;
                }
            }

            // END TEMP

            m_currentGopIndex = 0;
            m_currentFrameIndex = 0;
            m_currentGop_startFrameIndex = 0;
            m_currentGop_endFrameIndex = 0;
            m_frameCache_startGopIndex = 0;
            m_frameCache_endGopIndex = 0;
            m_frameCache_startFrameIndex = 0;
            m_frameCache_endFrameIndex = 0;

            GopRecord gop = m_gopList[m_gopList.Keys.Max()];
            m_maxFrameIndex = gop.frameIndex + gop.numFrames - 1;

            return success;
        }

        public bool LoadGopIntoCache(int gopIndex)
        {
            bool success = true;

            // TEMP
            GopRecord gop;

            if (m_gopList.TryGetValue(gopIndex, out gop))
            {
                int firstFrameIndexInGop = gop.frameIndex;
                if (!m_frameCache.ContainsKey(firstFrameIndexInGop))
                {
                    // go get all frames for gop with a timestamp = gop.timestamp
                    // then add them to the m_frameCache -- which means decoding them
                    // and writing the decoded from to a file using the function
                    //  WriteFile(string filename, double timestamp, int width, int height, int depth, byte[] frameData)

                    double millisecondsBetweenFrames = 0.010;
                    for (int i = 0; i < gop.numFrames; i++)
                    {
                        double timestamp = gop.timestamp + i * millisecondsBetweenFrames;
                        string filename = BuildFilenameFromTimestamp(timestamp);
                        int frameIndex = gop.frameIndex + i;

                        string jpegFilename = Path.Combine("d:/temp1/frames", "img_" + (frameIndex + 1).ToString("D8") + ".jpg");
                        int width, height, depth;
                        byte[] data;
                        if (File.Exists(jpegFilename))
                        {
                            if (GetDecodedByteArray(jpegFilename, out width, out height, out depth, out data))
                            {
                                if (WriteFile(filename, timestamp, width, height, depth, data))
                                {
                                    m_frameCache.Add(frameIndex, new FrameRecord(timestamp, Path.Combine(m_cacheDirectory, filename)));
                                }
                                else
                                {
                                    success = false;
                                    break;
                                }
                            }
                            else
                            {
                                success = false;
                                break;
                            }
                        }
                        else
                        {
                            success = false;
                            break;
                        }
                    }
                }

            }
            // END TEMP

            return success;
        }

        public bool RemoveGopFromCache(int gopIndex)
        {
            bool success = true;
            
            GopRecord gop;

            if (m_gopList.TryGetValue(gopIndex, out gop))
            {
                for (int j = gop.frameIndex; j < gop.frameIndex + gop.numFrames; j++)
                {
                    FrameRecord frame = m_frameCache[j];
                    if (File.Exists(frame.filename)) File.Delete(frame.filename);
                    m_frameCache.Remove(j);
                }
            }
            else
            {
                success = false;
            }

            return success;
        }

        public int GetLowestIndexInVideoCache()
        {
            return m_frameCache.Keys.Min();            
        }

        public int GetHighestIndexInVideoCache()
        {
            return m_frameCache.Keys.Max();
        }

        public int GetMaxFrameIndex()
        {
            return m_maxFrameIndex;
        }

        public bool ReadFile(string filename, out double timestamp, out int width, out int height, out int depth, out byte[] frameData)
        {
            bool success = true;
            timestamp = 0.0f;
            width = 0;
            height = 0;
            depth = 0;
            frameData = null;
            FileStream fileStream = null;

            if (File.Exists(filename))
            {

                try
                {
                    // Create new FileInfo object and get the Length.
                    FileInfo f = new FileInfo(filename);
                    long numBytesInFrameData = f.Length - sizeof(double) - sizeof(int) - sizeof(int) - sizeof(int);

                    fileStream = new FileStream(filename, FileMode.Open);
                    m_reader = new BinaryReader(fileStream);

                    timestamp = m_reader.ReadDouble();
                    width = m_reader.ReadInt32();
                    height = m_reader.ReadInt32();
                    depth = m_reader.ReadInt32();
                    long frameSize = width * height * depth;

                    if (numBytesInFrameData == frameSize)
                    {
                        frameData = m_reader.ReadBytes(width * height * depth);
                    }
                    else
                    {
                        OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Image File not correct size"));
                        success = false;
                    }

                }
                catch (Exception ex)
                {
                    OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, ex.Message));
                    success = false;
                }
                finally
                {
                    m_writer.Close();
                    m_writer.Dispose();
                    if (fileStream != null) fileStream.Dispose();
                }
            }
            else
            {
                success = false;
            }

            return success;
        }

        public bool WriteFile(string filename, double timestamp, int width, int height, int depth, byte[] frameData)
        {
            bool success = true;

            try
            {
                m_writer = new BinaryWriter(new FileStream(filename, FileMode.Create));

                m_writer.Write(timestamp);
                m_writer.Write(width);
                m_writer.Write(height);
                m_writer.Write(depth);
                m_writer.Write(frameData);
            }
            catch (Exception ex)
            {
                OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, ex.Message));
                success = false;
            }
            finally
            {
                m_writer.Close();
            }

            return success;
        }
                
        public bool GetFrame(int index, out double timestamp, out int width, out int height, out int depth, out byte[] frameData)
        {
            bool success = true;
            timestamp = 0;
            width = 0;
            height = 0;
            depth = 0;
            frameData = null;
            int currentFrameIndex_backup = m_currentFrameIndex;
            m_currentFrameIndex = index;

            // try to get the frame
            FrameRecord frame;
            if(m_frameCache.TryGetValue(index, out frame))
            {
                success = ReadFile(frame.filename, out timestamp, out width, out height, out depth, out frameData);
                if (success)
                {
                    // check to see if we crossed a gop boundary.  If so, we need to update the cache
                    UpdateCache();
                }
                else
                {
                    m_currentFrameIndex = currentFrameIndex_backup;
                    OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Failed to Load Frame from Cache\n" +
                          index.ToString()));
                }
            }
            else
            {   // frame index not in cache
                UpdateCache();
                if (m_frameCache.TryGetValue(index, out frame))
                {
                    success = ReadFile(frame.filename, out timestamp, out width, out height, out depth, out frameData);
                    if (success)
                    {
                        
                    }
                    else
                    {
                        m_currentFrameIndex = currentFrameIndex_backup;
                        success = false;
                        OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Failed to Load Frame from Cache\n" +
                              index.ToString()));
                    }
                }
                else
                {
                    m_currentFrameIndex = currentFrameIndex_backup;
                    success = false;
                    OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Failed to Find Frame in Cache\n" +
                          index.ToString()));
                }
            }

    
            return success;
        }
        
        public void InitCache()
        {
            // clear cache if it already exists
            m_frameCache.Clear();
            if (Directory.Exists(m_cacheDirectory))
            {
                // clear the directory
                string[] files = Directory.GetFiles(m_cacheDirectory, "*.*");
                foreach (string file in files) File.Delete(file);
            }
            else
            {
                Directory.CreateDirectory(m_cacheDirectory);
            }


            for (int i = 0; i < m_padding + 1; i++)
                LoadGopIntoCache(i);


            // reset variables
            m_currentGopIndex = 0;
            GopRecord gop = m_gopList[m_currentGopIndex];
            m_currentGop_startFrameIndex = gop.frameIndex;
            m_currentGop_endFrameIndex = gop.frameIndex + gop.numFrames - 1;
            m_frameCache_startGopIndex = 0;
            m_frameCache_endGopIndex = m_padding;
            m_frameCache_startFrameIndex = m_gopList[m_frameCache_startGopIndex].frameIndex;
            gop = m_gopList[m_frameCache_endGopIndex];
            m_frameCache_endFrameIndex = gop.frameIndex + gop.numFrames - 1;
        }

        public void UpdateCache()
        {
            // NOTE: 
            // make sure that m_currentFrameIndex has been updated to the desired index before calling this function
            //
            // make sure to update:
            //      m_currentGopIndex
            //      m_currentGop_startFrameIndex
            //      m_currentGop_endFrameIndex
            //      m_frameCache_startGopIndex
            //      m_frameCache_endGopIndex
            //      m_frameCache_startFrameIndex
            //      m_frameCache_endFrameIndex

            if (m_currentFrameIndex < m_currentGop_startFrameIndex)
            {
                // m_currentFrameIndex is BEFORE start frame cache, so correct frame cache to cover this frame index

                // find GOP index that contains the current frame index, and call it "targetGopIndex"
                int gopIndex = m_frameCache_startGopIndex;
                int targetGopIndex = -1;
                int maxGopIndex = m_gopList.Keys.Max();
                bool done = false;
                bool found = false;
                while(!done)
                {
                    GopRecord gop;
                    if(m_gopList.TryGetValue(gopIndex, out gop))
                    {
                        if(gop.frameIndex <= m_currentFrameIndex)
                        {
                            targetGopIndex = gopIndex;
                            found = true;
                            done = true;
                        }
                    }

                    gopIndex--;
                    if(gopIndex < 0)
                    {
                        done = true;
                    }
                }

                if(found)
                {
                    // build list of gops to load

                    int startGopIndex = targetGopIndex;
                    while(startGopIndex > 0 &&  targetGopIndex - startGopIndex < m_padding)
                    {
                        startGopIndex--;
                    }
                    
                    int endGopIndex = targetGopIndex;
                    while(endGopIndex < maxGopIndex && endGopIndex - targetGopIndex < m_padding)
                    {
                        endGopIndex++;
                    }
                    

                    // add new gops to cache
                    for (int i = startGopIndex; i <= endGopIndex; i++)
                        LoadGopIntoCache(i);

                    // remove gops from cache that are outside [startGopIndex -> endGopIndex]
                    for (int i = m_frameCache_startGopIndex; i <= m_frameCache_endGopIndex; i++)
                    {
                        if(i<startGopIndex || i>endGopIndex)
                        {
                            RemoveGopFromCache(i);
                        }
                    }

                    // reset variables
                    m_currentGopIndex = targetGopIndex;
                    GopRecord gop = m_gopList[m_currentGopIndex];
                    m_currentGop_startFrameIndex = gop.frameIndex;
                    m_currentGop_endFrameIndex = gop.frameIndex + gop.numFrames - 1;
                    m_frameCache_startGopIndex = startGopIndex;
                    m_frameCache_endGopIndex = endGopIndex;
                    m_frameCache_startFrameIndex = m_gopList[m_frameCache_startGopIndex].frameIndex;
                    gop = m_gopList[m_frameCache_endGopIndex];
                    m_frameCache_endFrameIndex = gop.frameIndex + gop.numFrames - 1;
                }
                else
                {
                    OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR,
                        "Failed to find GOP containing Frame Number: " + m_currentFrameIndex.ToString()));
                }
            }
            else if (m_currentFrameIndex > m_currentGop_endFrameIndex)
            {
                // m_currentFrameIndex is AFTER start frame cache, so correct frame cache to cover this frame index
                
                // find GOP index that contains the current frame index, and call it "targetGopIndex"
                int gopIndex = m_frameCache_endGopIndex;
                int targetGopIndex = -1;
                int maxGopIndex = m_gopList.Keys.Max();
                bool done = false;
                bool found = false;
                while (!done)
                {
                    GopRecord gop;
                    if (m_gopList.TryGetValue(gopIndex, out gop))
                    {
                        if ((gop.frameIndex + gop.numFrames - 1) >= m_currentFrameIndex)
                        {
                            targetGopIndex = gopIndex;
                            found = true;
                            done = true;
                        }
                    }

                    gopIndex++;
                    if (gopIndex > maxGopIndex)
                    {
                        done = true;
                    }
                }

                if (found)
                {
                    // build list of gops to load

                    int startGopIndex = targetGopIndex;
                    while (startGopIndex > 0 && targetGopIndex - startGopIndex < m_padding)
                    {
                        startGopIndex--;
                    }

                    int endGopIndex = targetGopIndex;
                    while (endGopIndex < maxGopIndex && endGopIndex - targetGopIndex < m_padding)
                    {
                        endGopIndex++;
                    }


                    // add new gops to cache
                    for (int i = startGopIndex; i <= endGopIndex; i++)
                    {   
                        LoadGopIntoCache(i);
                    }

                    // remove gops from cache that are outside [startGopIndex -> endGopIndex]
                    for (int i = m_frameCache_startGopIndex; i <= m_frameCache_endGopIndex; i++)
                    {
                        if (i < startGopIndex || i > endGopIndex)
                        {
                            RemoveGopFromCache(i);
                        }
                    }

                    // reset variables
                    m_currentGopIndex = targetGopIndex;
                    GopRecord gop = m_gopList[m_currentGopIndex];
                    m_currentGop_startFrameIndex = gop.frameIndex;
                    m_currentGop_endFrameIndex = gop.frameIndex + gop.numFrames - 1;
                    m_frameCache_startGopIndex = startGopIndex;
                    m_frameCache_endGopIndex = endGopIndex;
                    m_frameCache_startFrameIndex = m_gopList[m_frameCache_startGopIndex].frameIndex;
                    gop = m_gopList[m_frameCache_endGopIndex];
                    m_frameCache_endFrameIndex = gop.frameIndex + gop.numFrames - 1;
                }
                else
                {
                    OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR,
                        "Failed to find GOP containing Frame Number: " + m_currentFrameIndex.ToString()));
                }


            }
       

        } // END UpdateCache()



        ////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////
        // The following functions can be removed after testing


        public bool GetDecodedByteArray(string filename, out int width, out int height, out int depth, out byte[] data)
        {
            bool success = true;
            width = 0;
            height = 0;
            depth = 0;
            data = null;

            if (File.Exists(filename))
            {
                // Open a Stream and decode a JPEG image
                Stream imageStreamSource = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
                JpegBitmapDecoder decoder = new JpegBitmapDecoder(imageStreamSource, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
                BitmapSource bitmapSource = decoder.Frames[0];

                success = GetBytesFromBitmapSource(bitmapSource, out width, out height, out depth, out data);
            }
            else
            {
                success = false;
            }

            return success;
        }


        public bool GetBytesFromBitmapSource(BitmapSource bmp, out int width, out int height, out int depth, out byte[] data)
        {
            bool success = true;
            width = bmp.PixelWidth;
            height = bmp.PixelHeight;
            depth = ((bmp.Format.BitsPerPixel + 7) / 8);
            data = null;

            try
            {
                int stride = width * ((bmp.Format.BitsPerPixel + 7) / 8);
                data = new byte[height * stride];
                bmp.CopyPixels(data, stride, 0);
            }
            catch (Exception)
            {
                success = false;
            }

            return success;
        }





    } // END class VideoCache



    public enum VideoCache_Status_Type
    {
        OK,
        NEED_NEXT_GOP,
        NEED_PREV_GOP,
        ERROR
    }

    public class VideoCache_EventArgs : EventArgs
    {
        private VideoCache_Status_Type _status;
        public VideoCache_Status_Type status
        {
            get { return this._status; }
            set { this._status = value; }
        }

        private string _message;
        public string message
        {
            get { return this._message; }
            set { this._message = value; }
        }

        public VideoCache_EventArgs(VideoCache_Status_Type Status, string Message)
        {
            status = Status;
            message = Message;
        }
    }
}
