using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoTools
{
    // This class is used to cache a GOP group of images as files in a directory.
    //
    // Description:
    //
    //      1.  create an instance of this class with a given name.  This name is used to create a sub-directory
    //          to hold the cache files.
    //  
    //      2.  attach an event handler to the GOPCacheEvent. This event fires for one of several reasons: 
    //          a. you need to reload the cache with previous or next GOP
    //          b. an error has occurred
    //          
    //          the event arguments contain a enum value giving the event cause, along with a string message
    //
    //      3.  as you decode a GOP of images, call AddImageToCache(...) for each frame that is decoded.  This is how
    //          you build the cache.  There is a cache member variable that holds the current index.  This variable
    //          is used to step forward and backward in the cache.  The current index is initialized at 0 when the 
    //          cache is first created, or when Reset() is called.
    //  
    //      4.  when you're ready to retrieve an image from the cache, call one of the following:
    //          a. GetFirstFrame(...) - get the first frame in the GOP, likely the key frame. Sets current index to 0.
    //          b. GetLastFrame(...) - get the last frame in the GOP.  Sets the current index to the last frame.
    //          c. GetNextFrame(...) - trys to get frame at (current index + 1).  If this index is passed end of GOP, it
    //                                 return false and fires an event with the status = GOPCache_Status_Type.NEED_NEXT_GOP
    //          d. GetPrevFrame(...) - trys to get frame at (current index - 1).  If this index is less than 0, it
    //                                 return false and fires an event with the status = GOPCache_Status_Type.NEED_PREV_GOP
    //          e. GetFrame(...) - allows you to request a frame at a specified index.
    //
    //      5.  When you're finished editing/viewing the GOP, you can get a list of the files in the cache by calling
    //          GetOrderedFileList().  This returns a list of the cache files, sorted by timestamp. This can be used
    //          to re-encode the GOP, frame by frame, in the proper order when the files are read sequentially from the list.
    //       
    //      6.  To update a frame after it's been edited and is already in the cache, call UpdateCurrentFrame(...).
    //          Be sure to call this after editing a frame, like performing redaction on it, before calling GetNextFrame, 
    //          GetPrevFrame, etc.
    //   
    //      7.  When you want to reuse this cache with a new GOP, be sure to call Reset() which will clear the cache, and 
    //          reset the current index to 0.
    //
    //      8.  The GOPCache class destructor should clear all cached files and directories.




    public class GOPCache
    {
        BinaryReader m_reader;
        BinaryWriter m_writer;
        Dictionary<int, string> m_cacheDict;
        int m_currentIndex;        
        string m_cacheDirectory;
        string m_cacheName;

        // Events
        public delegate void GOPCache_EventHandler(object sender, GOPCache_EventArgs e);
        public event GOPCache_EventHandler GOPCacheEvent;
        protected virtual void OnGOPCacheEvent(GOPCache_EventArgs e)
        {
            if (GOPCacheEvent != null) GOPCacheEvent(this, e);
        }



        public GOPCache(string cacheName)
        {
            m_cacheName = cacheName;       

            m_cacheDict = new Dictionary<int, string>();

            m_currentIndex = 0;

            m_cacheDirectory = Path.Combine(Environment.CurrentDirectory,cacheName);

            DirectoryInfo dir;

            try
            {
                if (!Directory.Exists(m_cacheDirectory))
                {   
                    // since cache directory does not exist, create it
                    dir = Directory.CreateDirectory(m_cacheDirectory);
                }
                else
                {
                    // since cache directory already exists, make sure directory is empty
                    ClearCache();
                }
            }
            catch(Exception ex)
            {
                OnGOPCacheEvent(new GOPCache_EventArgs(GOPCache_Status_Type.ERROR, 
                    "Failed to intialize cache directory: " + m_cacheDirectory + "\n\n" + ex.Message));
            }
        }

        ~GOPCache()
        {
            if(Directory.Exists(m_cacheDirectory))
            {
                ClearCache();
                Directory.Delete(m_cacheDirectory);
            }
        }

        string GetFilename(int index)
        {
            return Path.Combine(m_cacheDirectory, "image_" + index.ToString("d5") + ".frame");
        }

        public bool GetFirstFrame(out double timestamp, out int width, out int height, out int depth, out byte[] frameData)
        {
            return GetFrame(0, out timestamp, out width, out height, out depth, out frameData);
        }

        public bool GetLastFrame(out double timestamp, out int width, out int height, out int depth, out byte[] frameData)
        {
            int ndx = m_cacheDict.Keys.Max();
            return GetFrame(ndx, out timestamp, out width, out height, out depth, out frameData);
        }

        public bool GetNextFrame(out double timestamp, out int width, out int height, out int depth, out byte[] frameData)
        {
            int ndx = m_currentIndex + 1;
            return GetFrame(ndx, out timestamp, out width, out height, out depth, out frameData);        
        }

        public bool GetPrevFrame(out double timestamp, out int width, out int height, out int depth, out byte[] frameData)
        { 
            int ndx = m_currentIndex - 1;
            return GetFrame(ndx, out timestamp, out width, out height, out depth, out frameData);          
        }

        public bool GetFrame(int index, out double timestamp, out int width, out int height, out int depth, out byte[] frameData)
        {
            bool success = true;
            timestamp = 0;
            width = 0;
            height = 0;
            depth = 0;
            frameData = null;
            
            if(m_cacheDict.ContainsKey(index))
            {                
                string filename = GetFilename(index);
                success = ReadFile(filename, out timestamp, out width, out height, out depth, out frameData);
                if(success)
                {
                    m_currentIndex = index;
                }
            }
            else
            {
                success = false;

                if(index > m_cacheDict.Keys.Max())
                    OnGOPCacheEvent(new GOPCache_EventArgs(GOPCache_Status_Type.NEED_NEXT_GOP, "Waiting for next GOP"));
                else if(index < 0)
                    OnGOPCacheEvent(new GOPCache_EventArgs(GOPCache_Status_Type.NEED_PREV_GOP, "Waiting for previous GOP"));
            }
            
            return success;
        }

        public bool AddImageToCache(double timestamp, int width, int height, int depth, byte[] decodedFrameData)
        {
            bool success = true;
            int numFramesInCache = m_cacheDict.Count;     
            string filename = GetFilename(numFramesInCache);            

            success = WriteFile(filename, timestamp, width, height, depth, decodedFrameData);
            if(success)
            {
                m_cacheDict.Add(numFramesInCache, filename);
            }

            return success;
        }
        
        public bool UpdateCurrentFrame(double timestamp, int width, int height, int depth, byte[] decodedFrameData)
        {
            bool success = true;
            int numFramesInCache = m_cacheDict.Count;
            string filename = GetFilename(m_currentIndex);

            if (File.Exists(filename)) File.Delete(filename);

            success = WriteFile(filename, timestamp, width, height, depth, decodedFrameData);
            if (success)
            {
                m_cacheDict.Add(numFramesInCache, filename);
            }

            return success;
        }

        public void Reset()
        {
            ClearCache();
            m_currentIndex = 0;
        }
        
        private void ClearCache()
        {
            try
            {
                foreach(KeyValuePair<int,string> item in m_cacheDict)
                {
                    File.Delete(item.Value);
                }

                m_cacheDict.Clear();

                // make sure directory is empty
                string[] files = Directory.GetFiles(m_cacheDirectory);
                foreach (string filename in files) File.Delete(filename);
            }
            catch(Exception ex)
            {
                OnGOPCacheEvent(new GOPCache_EventArgs(GOPCache_Status_Type.ERROR,
                    "Failed to clear cache directory: " + m_cacheDirectory + "\n\n" + ex.Message));
            }
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

                    if(numBytesInFrameData == frameSize)
                    {
                        frameData = m_reader.ReadBytes(width * height * depth);
                    }
                    else
                    {
                        OnGOPCacheEvent(new GOPCache_EventArgs(GOPCache_Status_Type.ERROR, "Image File not correct size"));
                        success = false;
                    }
                                       
                }
                catch (Exception ex)
                {
                    OnGOPCacheEvent(new GOPCache_EventArgs(GOPCache_Status_Type.ERROR, ex.Message));
                    success = false;
                }
                finally
                {
                    m_writer.Close();
                    m_writer.Dispose();
                    if(fileStream != null) fileStream.Dispose();
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
            catch(Exception ex)
            {
                OnGOPCacheEvent(new GOPCache_EventArgs(GOPCache_Status_Type.ERROR, ex.Message));
                success = false;
            }
            finally
            {
                m_writer.Close();
            }

            return success;
        }

        public List<string> GetOrderedFileList()
        {
            // returns a list of the cache files in timestamp order (earliest to latest)

            // you might need to call this to re-encode the frames in the GOP

            List<string> fileList = new List<string>();

            var list = m_cacheDict.Keys.ToList();
            list.Sort();

            foreach (int ndx in list)
                fileList.Add(m_cacheDict[ndx]);

            return fileList;
        }
       
        public void GetCurrentIndex(ref int currentIndex, ref int maxIndex)
        {
            currentIndex = m_currentIndex;
            maxIndex = m_cacheDict.Keys.Max();
        }

        public void TriggerEvent(GOPCache_Status_Type eventType, string message)
        {
            OnGOPCacheEvent(new GOPCache_EventArgs(eventType, message));
        }
    }

   


    public enum GOPCache_Status_Type
    {
        OK,
        NEED_NEXT_GOP,
        NEED_PREV_GOP,
        ERROR       
    }

    public class GOPCache_EventArgs : EventArgs
    {
        private GOPCache_Status_Type _status;
        public GOPCache_Status_Type status
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
        
        public GOPCache_EventArgs(GOPCache_Status_Type Status, string Message)
        {
            status = Status;
            message = Message;          
        }
    }
}
