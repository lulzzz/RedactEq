using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace VideoTools
{
    public enum FRAME_EDIT_TYPE
    {
        REDACTION,
        TRACKING
    }

   
    public class BoundingBox
    {
        private int _x1;
        public int x1
        {
            get { return _x1; }
            set { _x1 = value; }
        }

        private int _y1;
        public int y1
        {
            get { return _y1; }
            set { _y1 = value; }
        }

        private int _x2;
        public int x2
        {
            get { return _x2; }
            set { _x2 = value; }
        }

        private int _y2;
        public int y2
        {
            get { return _y2; }
            set { _y2 = value; }
        }

        public BoundingBox(int X1, int Y1, int X2, int Y2)
        {
            x1 = X1;
            y1 = Y1;
            x2 = X2;
            y2 = Y2;
        }

    }

 
    public class FrameEdit
    {
        private FRAME_EDIT_TYPE _type;
        public FRAME_EDIT_TYPE type
        {
            get { return _type; }
            set { _type = value; }
        }

        private BoundingBox _box;
        public BoundingBox box
        {
            get { return _box; }
            set { _box = value; }
        }

        public FrameEdit(FRAME_EDIT_TYPE Type, BoundingBox BBox)
        {
            type = Type;
            box = BBox;
        }
    }


   


    public class VideoEditsDatabase
    {
        string m_mp4Filename;
        string m_databaseFilename;
        string m_errorMsg;

        public ObservableConcurrentDictionary<double, ObservableCollection<FrameEdit>> m_editsDictionary;

        // Events
        public delegate void VideoEditsDatabase_EventHandler(object sender, VideoEditsDatabase_EventArgs e);
        public event VideoEditsDatabase_EventHandler VideoEditsDatabaseEvent;
        protected virtual void OnVideoEditsDatabaseEvent(VideoEditsDatabase_EventArgs e)
        {
            if (VideoEditsDatabaseEvent != null) VideoEditsDatabaseEvent(this, e);
        }

        public VideoEditsDatabase(string mp4Filename)
        {
            if (File.Exists(mp4Filename))
            {
                m_mp4Filename = mp4Filename;
                m_databaseFilename = BuildVideoEditsDatabaseFilename(m_mp4Filename);

                m_editsDictionary = new ObservableConcurrentDictionary<double, ObservableCollection<FrameEdit>>();

                if (File.Exists(m_databaseFilename))
                {
                    if (ReadDatabase())
                    {
                        OnVideoEditsDatabaseEvent(new VideoEditsDatabase_EventArgs(false, "Reading Existing Edit Database:\n" +
                            m_databaseFilename));
                    }
                }
            }
            else
            {
                OnVideoEditsDatabaseEvent(new VideoEditsDatabase_EventArgs(true, "File does not exist:\n" + mp4Filename));
            }
        }

        public string BuildVideoEditsDatabaseFilename(string mp4Filename)
        {
            return mp4Filename.Replace(".mp4", "_editsDB.dat");
        }


        public void AddFrameEdit(double timestamp, FRAME_EDIT_TYPE type, BoundingBox bbox)
        {
            if(m_editsDictionary.ContainsKey(timestamp))
            {
                ObservableCollection<FrameEdit> list = m_editsDictionary[timestamp];
                list.Add(new FrameEdit(type,bbox));
            }
            else
            {
                ObservableCollection<FrameEdit> list = new ObservableCollection<FrameEdit>();
                list.Add(new FrameEdit(type, bbox));
                m_editsDictionary.Add(timestamp, list);
            }
        }


        public void DeleteFrameEdit(long timestamp, FRAME_EDIT_TYPE type, BoundingBox bbox)
        {
            if(m_editsDictionary.ContainsKey(timestamp))
            {
                ObservableCollection<FrameEdit> list = m_editsDictionary[timestamp];
                bool found = false;
                foreach(FrameEdit fe in list)
                {
                    if(fe.box.x1 == bbox.x1 &&
                       fe.box.y1 == bbox.y1 &&
                       fe.box.x2 == bbox.x2 &&
                       fe.box.y2 == bbox.y2)
                    {
                        found = true;
                        list.Remove(fe);
                    }
                }
                if(!found)
                {
                    OnVideoEditsDatabaseEvent(new VideoEditsDatabase_EventArgs(true, "Failed to remove Frame Edit:\n" +
                    "given bounding box not found for timestamp"));
                }
            }
            else
            {
                OnVideoEditsDatabaseEvent(new VideoEditsDatabase_EventArgs(true, "Failed to remove Frame Edit:\n" +
                    "given timestamp not found"));
            }
        }


        public ObservableCollection<FrameEdit> GetRedactionListForTimestamp(double timestamp)
        {
            ObservableCollection<FrameEdit> list;

            if (m_editsDictionary.ContainsKey(timestamp))
            {
                list = m_editsDictionary[timestamp];                
            }
            else
            {
                list = new ObservableCollection<FrameEdit>();
                m_editsDictionary.Add(timestamp, list);
            }

            return list;
        }


        public void SetAllFrameEditsForFrame(long timestamp, ObservableCollection<FrameEdit> edits)
        {
            DeleteAllFrameEditsForFrame(timestamp);

            m_editsDictionary.Add(timestamp, edits);
        }

        public void DeleteAllFrameEditsForFrame(long timestamp)
        {
            if (m_editsDictionary.ContainsKey(timestamp))
            {
                m_editsDictionary.Remove(timestamp);
            }
        }

        public ObservableCollection<FrameEdit> GetEditsForFrame(long timestamp)
        {
            if (m_editsDictionary.ContainsKey(timestamp))
            {
                return m_editsDictionary[timestamp];       
            }
            else
            {
                return new ObservableCollection<FrameEdit>();
            }
        }


        public bool SaveDatabase()
        {
            bool success = true;
            try
            {
                using (BinaryWriter bw = new BinaryWriter(new FileStream(m_databaseFilename, FileMode.Create, FileAccess.Write)))
                {
                    bw.Write(m_editsDictionary.Keys.Count); // write number of frames with edits

                    // iterate each frame with edits
                    foreach (KeyValuePair<double, ObservableCollection<FrameEdit>> frame in m_editsDictionary)
                    {
                        bw.Write(frame.Key);  // write timestamp
                        bw.Write(frame.Value.Count);  // write number of FrameEdits for this frame

                        foreach (FrameEdit fe in frame.Value)
                        {
                            bw.Write((int)fe.type);
                            bw.Write(fe.box.x1);
                            bw.Write(fe.box.y1);
                            bw.Write(fe.box.x2);
                            bw.Write(fe.box.y2);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_errorMsg = ex.Message;
                success = false;
                OnVideoEditsDatabaseEvent(new VideoEditsDatabase_EventArgs(true,m_errorMsg));
            }

            return success;
        }


        public bool ReadDatabase()
        {
            bool success = true;
            try
            {
                if (File.Exists(m_databaseFilename))
                {
                    m_editsDictionary = new ObservableConcurrentDictionary<double, ObservableCollection<FrameEdit>>();

                    using (BinaryReader br = new BinaryReader(new FileStream(m_databaseFilename, FileMode.Open)))
                    {
                        int numFrames = br.ReadInt32(); // read number of frames with edits

                        // iterate each frame with edits
                        for(int i = 0; i< numFrames; i++)
                        {
                            double timestamp = br.ReadDouble();
                            int numFrameEdits = br.ReadInt32();

                            ObservableCollection<FrameEdit> feList = new ObservableCollection<FrameEdit>();

                            for(int j = 0; j < numFrameEdits; j++)
                            {
                                FRAME_EDIT_TYPE type = (FRAME_EDIT_TYPE)br.ReadInt32();
                                int x1 = br.ReadInt32();
                                int y1 = br.ReadInt32();
                                int x2 = br.ReadInt32();
                                int y2 = br.ReadInt32();

                                FrameEdit fe = new FrameEdit(type, new BoundingBox(x1, y1, x2, y2));

                                feList.Add(fe);
                            }

                            m_editsDictionary.Add(timestamp, feList);
                        }
                    }
                }
                else
                {
                    success = false;
                    m_errorMsg = "File does not exist: " + m_databaseFilename;
                    OnVideoEditsDatabaseEvent(new VideoEditsDatabase_EventArgs(true, m_errorMsg));
                }
            }
            catch (Exception ex)
            {
                m_errorMsg = ex.Message;
                success = false;
                OnVideoEditsDatabaseEvent(new VideoEditsDatabase_EventArgs(true, m_errorMsg));
            }

            return success;
        }

        public string GetLastError()
        {
            return m_errorMsg;
        }
        
    }

    

    public class VideoEditsDatabase_EventArgs : EventArgs
    {
        private bool _isError;
        public bool isError
        {
            get { return this._isError; }
            set { this._isError = value; }
        }

        private string _message;
        public string message
        {
            get { return this._message; }
            set { this._message = value; }
        }

        public VideoEditsDatabase_EventArgs(bool IsError, string Message)
        {
            isError = IsError;
            message = Message;
        }
    }


}
