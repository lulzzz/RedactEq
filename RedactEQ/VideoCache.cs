using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Equature.Integration;
using System.Threading.Tasks.Dataflow;

namespace VideoTools
{
    public class FrameRecord
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

    public class GopRecord
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
        public SortedList<int,GopRecord> m_gopList;  // sorted list of all gop timestamps in mp4 file
                                              // key = frame index (within entire movie) for the key frame of this gop
                                              // value = GopRecord containing timestamp of key frame and numFrames in gop

        public SortedList<int,FrameRecord> m_frameCache; // key = frame index within entire movie, value = filename

        public Dictionary<double, int> m_gopIndexLookup; // a convenience dictionary that makes it easy to get the frame index of a GOP give a timestamp

        int m_padding;
        string m_mp4Filename;
        string m_cacheDirectory;
        BinaryReader m_reader;
        BinaryWriter m_writer;

        int m_maxFrameIndex;

        IntPtr m_mp4Reader;

        List<int> m_gopsInCache;

        long m_durationMilliseconds;
        double m_frameRate;        
        int m_height;
        int m_width;
        int m_sampleCount;
        int m_targetWidth;
        int m_targetHeight;

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
            m_frameCache = new SortedList<int, FrameRecord>();
            m_gopIndexLookup = new Dictionary<double, int>();
            m_gopsInCache = new List<int>();
            InitCache();                      
        }

        ~VideoCache()
        {
            try
            {
                if (Directory.Exists(m_cacheDirectory))
                    Directory.Delete(m_cacheDirectory);

                if (m_mp4Reader != IntPtr.Zero)
                    Mp4.DestroyMp4Reader(m_mp4Reader);
            }
            catch
            { }
        }

        public bool Init(int targetWidth, int targetHeight)
        {
            bool success = true;

            m_targetWidth = targetWidth;
            m_targetHeight = targetHeight;

            if (m_mp4Reader != IntPtr.Zero)
            {
                Mp4.DestroyMp4Reader(m_mp4Reader);
                m_mp4Reader = IntPtr.Zero;
            }

            m_mp4Reader = Mp4.CreateMp4Reader(m_mp4Filename);
            if(m_mp4Reader != IntPtr.Zero)
            {

                // Get the video metadata from the file
                if (!Mp4.GetVideoProperties(m_mp4Reader, out m_durationMilliseconds,
                                           out m_frameRate, out m_width, out m_height, out m_sampleCount))
                {
                    OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Failed to get properties of this video file: " +
                        m_mp4Filename, null));
                    success = false;
                }
            }
            else
            {
                success = false;
            }

            return success;
        }

        public long GetVideoDuration()
        {
            return m_durationMilliseconds;
        }

        public int GetNumFramesInVideo()
        {
            //return m_sampleCount;  // TODO: Get Brad to fix this.  Get Video properties returns wrong number of frames in video


            GopRecord gop = m_gopList[m_gopList.Keys.Max()];
            int num = gop.frameIndex;
            return num;
        }

        public string GetCacheDirectory(string cacheName)
        {
            string dir = Environment.CurrentDirectory;
            return Path.Combine(dir, cacheName);
        }

        public string BuildFilenameFromTimestamp(double timestamp)
        {            
            return Path.Combine(m_cacheDirectory, ((int)(timestamp*1000.0)).ToString("d9") + ".frame");
        }

    
        public bool GetClosestFrameIndex(double time, out int frameIndex, out double timestamp, out double percentPosition)
        {
            bool success = false;
            int targetGopIndex = 0;
            frameIndex = 0;
            timestamp = 0.0;
            percentPosition = 0.0;

            if (m_gopList != null)
            {
                foreach (KeyValuePair<int, GopRecord> gop in m_gopList)
                {
                    if (gop.Value.timestamp >= time)
                    {
                        targetGopIndex = gop.Key;
                        frameIndex = gop.Value.frameIndex;
                        timestamp = gop.Value.timestamp;
                        double durationOfEntireVideo = (double)m_durationMilliseconds / 1000.0;
                        percentPosition = timestamp / durationOfEntireVideo * 100.0f;
                        success = true;
                        break;
                    }                   
                }
            }
           
            return success;
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
                        OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Image File not correct size", null));
                        success = false;
                    }

                }
                catch (Exception ex)
                {
                    OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, ex.Message, null));
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
                OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, ex.Message, null));
                success = false;
            }
            finally
            {
                if(m_writer != null)
                    m_writer.Close();
            }

            return success;
        }
                
  
        public void InitCache()
        {
            // clear cache if it already exists
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

        }



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


        /////////////////////////////////////////////////////////////////////
        /////////////////////////////////////////////////////////////////////
        /////////////////////////////////////////////////////////////////////
        
        public int GetGopIndexContainingFrameIndex(int frameIndex, SortedList<int,GopRecord> gopList)
        {
            int gopIndex = -1;
            foreach (KeyValuePair<int, GopRecord> item in gopList)
            {
                int index = item.Key;
                int lastIndexOfGop = item.Value.frameIndex + item.Value.numFrames - 1;
                if (lastIndexOfGop >= frameIndex)
                {
                    gopIndex = index;
                    break;
                }
            }

            return gopIndex;
        }

        public bool FindNearestGop(double timestamp, out KeyValuePair<double, int> item)
        {
            bool success = false;

            item = new KeyValuePair<double, int>(0,0);

            foreach (KeyValuePair<double, int> gop in m_gopIndexLookup)
            {
                if(timestamp >= gop.Key)
                {
                    success = true;
                    item = gop;
                }
            }

            return success;
        }


        public bool GetGopList(string mp4Filename, SortedList<int,GopRecord> gopList)
        {
            bool success = true;
           
            if (gopList == null) gopList = m_gopList;

            gopList.Clear();
            m_gopIndexLookup.Clear();

            if (m_mp4Reader != IntPtr.Zero)
            {
                double[] timestamps;
                Mp4.GetVideoKeyFrameTimestamps(m_mp4Reader, out timestamps);

                int[] framesInGop = new int[timestamps.Length]; // FIX
                Mp4.GetVideoGOPLengths(m_mp4Reader, out framesInGop);


                int ndx = 0;
                for (int i = 0; i < timestamps.Length; i++)
                {
                    gopList.Add(i, new GopRecord(ndx, timestamps[i], framesInGop[i]));
                    m_gopIndexLookup.Add(timestamps[i], ndx);
                    ndx += framesInGop[i];
                }
            }
            else
            {
                success = false;
            }

            if (gopList.Count > 0)
            {
                GopRecord gop = gopList[gopList.Keys.Max()];
                m_maxFrameIndex = gop.frameIndex + gop.numFrames - 1;
            }
            else
                m_maxFrameIndex = 0;

            return success;
        }


        public bool LoadGopIntoCache(int gopIndex, SortedList<int, GopRecord> gopList, SortedList<int, FrameRecord> frameCache)
        {
            bool success = true;

            // TEMP
            GopRecord gop;

            if (gopList.TryGetValue(gopIndex, out gop))
            {
                int firstFrameIndexInGop = gop.frameIndex;
                if (!frameCache.ContainsKey(firstFrameIndexInGop))
                {
                    // go get all frames for gop with a timestamp = gop.timestamp
                    // then add them to the m_frameCache -- which means decoding them
                    // and writing the decoded from to a file using the function
                    //  WriteFile(string filename, double timestamp, int width, int height, int depth, byte[] frameData)

                    // NEW
                    double actualPosition = Mp4.SetTimePositionAbsolute(m_mp4Reader, gop.timestamp + 0.001);
                    byte[] frame = new byte[m_targetWidth * m_targetHeight * 3];
                    bool key;
                    int ndx = 0;

                    if (!frameCache.ContainsKey(gop.frameIndex))
                        while (true)
                        {
                            double ts = (double)Mp4.GetNextVideoFrame(m_mp4Reader, frame, out key, m_targetWidth, m_targetHeight)/1000.0;

                            if (ts == -1)   // EOF                             
                            {
                                break;
                            }

                            string filename = BuildFilenameFromTimestamp(ts);
                            int frameIndex = gop.frameIndex + ndx;

                            if (WriteFile(filename, ts, m_targetWidth, m_targetHeight, 3, frame))
                            {
                                frameCache.Add(frameIndex, new FrameRecord(ts, Path.Combine(m_cacheDirectory, filename)));
                            }
                            else
                            {
                                success = false;
                                break;
                            }

                            ndx++;

                            if (ndx >= gop.numFrames)
                            {
                                break;
                            }
                        }


                }

            }
            // END TEMP

            return success;
        }

        public bool RemoveGopFromCache(int gopIndex, SortedList<int, GopRecord> gopList, SortedList<int, FrameRecord> frameCache)
        {
            bool success = true;

            GopRecord gop;

            if (gopList.TryGetValue(gopIndex, out gop))
            {
                for (int j = gop.frameIndex; j < gop.frameIndex + gop.numFrames; j++)
                {
                    FrameRecord frame = frameCache[j];
                    if (File.Exists(frame.filename)) File.Delete(frame.filename);
                    frameCache.Remove(j);
                }
            }
            else
            {
                success = false;
            }

            return success;
        }


        public ITargetBlock<Tuple<int>> CreateCacheUpdatePipeline(int padding)
        {
            SortedList<int, GopRecord> l_gopList = new SortedList<int, GopRecord>();
            SortedList<int, FrameRecord> l_frameCache = new SortedList<int, FrameRecord>();
            List<int> l_gopsLoaded = new List<int>();
            int l_padding = padding;
            int l_currentGOP = -1;


            FrameRecord frame;

            // initialize cache
            bool success1 = true;

            if (GetGopList(m_mp4Filename, l_gopList))
            {
                for (int i = 0; i < padding + 1; i++)
                {
                    if (LoadGopIntoCache(i, l_gopList, l_frameCache))
                    {
                        l_gopsLoaded.Add(i);
                    }
                    else
                    {
                        OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Error Intializing Cache", null));
                        success1 = false;
                        break;
                    }
                }
            }
            else
            {
                OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Error Intializing GOP List", null));
                success1 = false;                
            }

            // send first image in file
            if (success1)
            {
                l_currentGOP = 0;
                
                double timestamp;
                int width, height, depth;
                byte[] frameData;

                if (l_frameCache.TryGetValue(0, out frame))
                {                    
                    if (ReadFile(frame.filename, out timestamp, out width, out height, out depth, out frameData))
                    {

                        OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.FRAME, "",
                            new FramePackage(0, timestamp, 0, 0.0, new DNNTools.ImagePackage(frameData, timestamp, width, height, depth))));
                    }
                    else
                    {
                        OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Failed to read Image File from Cache: " +
                                                                    frame.filename, null));
                    }
                }
            }


        


            ///////////////////////////////////////////////////////////////////////////////////////////////////
            ///////////////////////////////////////////////////////////////////////////////////////////////////
            ///////////////////////////////////////////////////////////////////////////////////////////////////


            var RequestImage = new ActionBlock<Tuple<int>>(inputData =>
            {
                int frameIndex = inputData.Item1;

                bool success;
                double timestamp;
                int width, height, depth;
                byte[] frameData;


                if (l_frameCache.TryGetValue(frameIndex, out frame))
                {                    
                    success = ReadFile(frame.filename, out timestamp, out width, out height, out depth, out frameData);
                    if (success)
                    {
                        GopRecord gop = m_gopList[l_currentGOP];

                        OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.FRAME, "",
                            new FramePackage(frameIndex, timestamp, l_currentGOP, gop.timestamp, new DNNTools.ImagePackage(frameData, timestamp, width, height, depth))));
                    }
                    else
                    {
                        OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Failed to read Image File from Cache: " +
                                                                    frame.filename, null));
                    }                    
                }
                else
                {
                    // figure out which GOPs to load
                    int gopToLoad = GetGopIndexContainingFrameIndex(frameIndex, l_gopList);

                    if (gopToLoad != -1)  // if gop found, -1 indicates that it wasn't found
                    {
                        if (LoadGopIntoCache(gopToLoad, l_gopList, l_frameCache))
                        {
                            l_gopsLoaded.Add(gopToLoad);
                            l_gopsLoaded.Sort();

                            if (l_frameCache.TryGetValue(frameIndex, out frame))
                            {                                
                                if (ReadFile(frame.filename, out timestamp, out width, out height, out depth, out frameData))
                                {
                                    l_currentGOP = gopToLoad;

                                    GopRecord gop = m_gopList[l_currentGOP];

                                    OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.FRAME, "",
                                        new FramePackage(frameIndex, timestamp, l_currentGOP, gop.timestamp, new DNNTools.ImagePackage(frameData, timestamp, width, height, depth))));
                                }
                                else
                                {
                                    OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Failed to read Image File from Cache: " +
                                                                                frame.filename + "\nafter trying to load GOP into Cache.", null));
                                }
                            }

                            // check to see if cache has grown to be too large.  If so, remove a gop from cache
                            if(l_gopsLoaded.Count > (2*l_padding + 1))
                            {
                                int lowestGop = l_gopsLoaded[0];
                                int highestGop = l_gopsLoaded[l_gopsLoaded.Count - 1];

                                int interval1 = Math.Abs(l_currentGOP - lowestGop);
                                int interval2 = Math.Abs(highestGop - l_currentGOP);

                                if (interval1 > interval2)
                                {
                                    if (RemoveGopFromCache(lowestGop, l_gopList, l_frameCache))
                                        l_gopsLoaded.RemoveAt(0);
                                }
                                else
                                {
                                    if (RemoveGopFromCache(highestGop, l_gopList, l_frameCache))
                                        l_gopsLoaded.RemoveAt(l_gopsLoaded.Count - 1);
                                }
                            }
                        }
                        else
                        {
                            OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.ERROR, "Failed to Load GOP " + gopToLoad.ToString(), null));
                        }
                    }
                    else
                    {
                        OnVideoCacheEvent(new VideoCache_EventArgs(VideoCache_Status_Type.REACHED_END_OF_FILE, "Reached EOF", null));
                    }
        
                }

            });



            return RequestImage;
        }

        /////////////////////////////////////////////////////////////////////
        /////////////////////////////////////////////////////////////////////
        /////////////////////////////////////////////////////////////////////


    } // END class VideoCache



    public enum VideoCache_Status_Type
    {
        FRAME,
        ERROR,
        REACHED_END_OF_FILE,
        REACHED_BEGINNING_OF_FILE
    }

    public class FramePackage
    {
        public int frameIndex;
        public double timestamp;
        public int gopIndex;
        public double gopTimestamp;
        public DNNTools.ImagePackage imagePackage;
        public FramePackage(int FrameIndex, double TimeStamp, int GopIndex, double GopTimestamp, DNNTools.ImagePackage ImgPackage)
        {
            frameIndex = FrameIndex;
            timestamp = TimeStamp;
            gopIndex = GopIndex;
            gopTimestamp = GopTimestamp;
            imagePackage = ImgPackage;
        }
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

        private FramePackage _frame;
        public FramePackage frame
        {
            get { return this._frame; }
            set { this._frame = value; }
        }


        public VideoCache_EventArgs(VideoCache_Status_Type Status, string Message, FramePackage framePackage)
        {
            status = Status;
            message = Message;
            frame = framePackage;
        }
    }
}
