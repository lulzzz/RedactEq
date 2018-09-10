using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoTools;
using DNNTools;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using TensorFlow;

namespace RedactEQ
{
    
   

    public partial class MainWindow : Window
    {
        string m_errorMsg;
  
        VideoCache m_videoCache;                                    

        MainViewModel m_vm;


        private DNNengine m_engine;
        CancellationTokenSource m_cancelTokenSource;
        WPFTools.PauseTokenSource m_pauseTokenSource;
        TaskScheduler m_uiTask;
        int m_analysisWidth, m_analysisHeight;

        ITargetBlock<Tuple<byte[], double, int, int, int, WriteableBitmap, bool>> m_pipeline;

        VideoEditsDatabase m_editsDB;
        int m_currentFrameIndex;
        int m_maxFrameIndex;
        bool m_dragging;
        Point m_p1, m_p2;
        

        public MainWindow()
        {
            InitializeComponent();
            m_vm = new MainViewModel();
            DataContext = m_vm;
            m_currentFrameIndex = 0;

            m_analysisHeight = 480;
            m_analysisWidth = 640;
            
            m_uiTask = TaskScheduler.FromCurrentSynchronizationContext();

            if (InitDNN("D:/tensorflow/pretrained_models/Golden/1/frozen_inference_graph.pb",
                    "D:/tensorflow/pretrained_models/Golden/1/face_label_map.pbtxt"))
            {
                //TestImage("d:/Pictures/kids_2.jpg");
            }
            else
            {
                MessageBox.Show("Failed to Initialize DNN: " + m_errorMsg, "DNN Engine Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }

        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (m_editsDB != null) m_editsDB.SaveDatabase();
        }

        public bool InitDNN(string modelFile, string catalogFile)
        {
            bool success = true;
            //string _modelPath = "D:/tensorflow/pretrained_models/Golden/1/frozen_inference_graph.pb";
            //string _catalogPath = "D:/tensorflow/pretrained_models/Golden/1/label_map.pbtxt";
            //string _imageFile = "D:/Pictures/Kids_2.jpg";

            try
            {
                Dictionary<int, string> classes = new Dictionary<int, string>();

                //LoadImageFromFile(_imageFile);

                List<CatalogItem> items = CatalogUtil.ReadCatalogItems1(catalogFile);
                if (items.Count > 0)
                {
                    classes.Clear();
                    foreach (CatalogItem item in items)
                    {
                        classes.Add(item.Id, item.Name);
                    }


                    m_engine = new DNNTools.DNNengine();
                    success = m_engine.Init(modelFile, classes);

                    m_cancelTokenSource = new CancellationTokenSource();

                    m_pipeline = m_engine.CreateDNNPipeline(modelFile, classes, m_editsDB, m_analysisWidth, m_analysisHeight, TFDataType.UInt8, 0.50f, null,null,
                                                            m_uiTask, m_cancelTokenSource.Token);
                }
                else
                {
                    MessageBox.Show("Failed to read class label file: " + catalogFile, "Error Reading Catalog File",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    success = false;
                }
            }
            catch (Exception ex)
            {
                success = false;
                m_errorMsg = ex.Message;
            }

            return success;
        }




        private void m_cacheOriginal_GOPCacheEvent(object sender, GOPCache_EventArgs e)
        {            
            switch(e.status)
            {
                case GOPCache_Status_Type.ERROR:
                    MessageBox.Show(e.message);
                    break;
                case GOPCache_Status_Type.NEED_NEXT_GOP:                    
                    break;
                case GOPCache_Status_Type.NEED_PREV_GOP:                   
                    break;
                case GOPCache_Status_Type.OK:
                    break;
            }
        }


        private void OpenItem_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "MP4 (*.mp4)|*.mp4";
            openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (openFileDialog.ShowDialog() == true)
            {
                m_vm.mp4Filename = openFileDialog.FileName;

                m_videoCache = new VideoCache("VideoCache", m_vm.mp4Filename, 2);
                m_videoCache.Init();
                m_maxFrameIndex = m_videoCache.GetMaxFrameIndex();

                // create new video edits database and bind to listview

                if (m_editsDB != null) m_editsDB.SaveDatabase();

                m_editsDB = new VideoEditsDatabase(m_vm.mp4Filename);

                double timestamp;
                int width, height, depth;
                byte[] data;
                if(m_videoCache.GetFrame(0, out timestamp, out width, out height, out depth, out data))
                {
                    Update_Manual(timestamp, width, height, depth, data);

                    m_vm.redactions = m_editsDB.GetRedactionListForTimestamp(timestamp);
                    m_vm.RedrawRedactionBoxes_Manual();

                    Update_Auto(timestamp, width, height, depth, data);
                    m_vm.RedrawRedactionBoxes_Auto();
                }

                PlayerRibbon_PlayPB.IsEnabled = true;
                AutoRedactRibbon_PlayPB.IsEnabled = true;

                m_vm.state = AppState.READY;

            }
        }


        private void ExportItem_Click(object sender, RoutedEventArgs e)
        {

        }



        private void ExitItem_Click(object sender, RoutedEventArgs e)
        {            
            Close();
        }

  

        private string BuildEditedFilename(string filename)
        {
            string editedFilename = filename;

            editedFilename.Replace(".mp4", "_edited.mp4");

            return editedFilename;
        }


        private void File_Exit_Menu_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ManualOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {            
            m_dragging = true;
            m_p1 = ConvertToOverlayPosition(e.GetPosition(ManualOverlay), ManualImage, m_vm.manualOverlay);
            m_p2 = m_p1;                        
        }

        private void ManualOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            m_dragging = false;
            int x1, x2, y1, y2;
            GetDrawPoints(out x1, out y1, out x2, out y2);

            if (m_editsDB != null)
                m_editsDB.AddFrameEdit(m_vm.timestamp, FRAME_EDIT_TYPE.REDACTION, new VideoTools.BoundingBox(x1,y1,x2,y2));

            m_vm.RedrawRedactionBoxes_Manual();
                
        }

        private void ManualOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (m_dragging)
            {
                int x1, x2, y1, y2;

                m_vm.RedrawRedactionBoxes_Manual();

                // draw new (the one we're dragging that is not yet in the collection)
                m_p2 = ConvertToOverlayPosition(e.GetPosition(ManualOverlay), ManualImage, m_vm.manualOverlay);
                GetDrawPoints(out x1, out y1, out x2, out y2);
                m_vm.manualOverlay.FillRectangle(x1, y1, x2, y2, m_vm.fillColor);
                //m_vm.manualOverlay.DrawRectangle(x1+1, y1+1, x2-1, y2-1, Colors.Red);
                //m_vm.manualOverlay.DrawRectangle(x1+2, y1+2, x2-2, y2-2, Colors.Red);                
            }
        }

        private void ManualOverlay_MouseLeave(object sender, MouseEventArgs e)
        {
            if (m_dragging)
            {
                m_vm.manualOverlay.Clear();
                Int32Rect rect = new Int32Rect(0, 0, (int)m_vm.manualOverlay.Width, (int)m_vm.manualOverlay.Height);
            }

            m_dragging = false;
        }


        private void ManualOverlay_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Point pt = ConvertToOverlayPosition(e.GetPosition(ManualOverlay), ManualImage, m_vm.manualOverlay);

            foreach(FrameEdit fe in m_vm.redactions)
            {
                int x = (int)pt.X;
                int y = (int)pt.Y;
                if(x > fe.box.x1 && x < fe.box.x2 && y > fe.box.y1 && y < fe.box.y2)
                {
                    m_vm.redactions.Remove(fe);
                    break;
                }
            }

            m_vm.RedrawRedactionBoxes_Manual();
        }



        private void AutoOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            m_dragging = true;
            m_p1 = ConvertToOverlayPosition(e.GetPosition(AutoOverlay), AutoImage, m_vm.autoOverlay);
            m_p2 = m_p1;
        }

        private void AutoOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            m_dragging = false;
            int x1, x2, y1, y2;
            GetDrawPoints(out x1, out y1, out x2, out y2);

            if (m_editsDB != null)
                m_editsDB.AddFrameEdit(m_vm.timestamp, FRAME_EDIT_TYPE.REDACTION, new VideoTools.BoundingBox(x1, y1, x2, y2));
            
            m_vm.RedrawRedactionBoxes_Auto();

        }

        private void AutoOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (m_dragging)
            {
                int x1, x2, y1, y2;

                m_vm.RedrawRedactionBoxes_Auto();

                // draw new (the one we're dragging that is not yet in the collection)
                m_p2 = ConvertToOverlayPosition(e.GetPosition(AutoOverlay), AutoImage, m_vm.autoOverlay);
                GetDrawPoints(out x1, out y1, out x2, out y2);
                m_vm.autoOverlay.FillRectangle(x1, y1, x2, y2, m_vm.fillColor);            
            }
        }

        private void AutoOverlay_MouseLeave(object sender, MouseEventArgs e)
        {
            if (m_dragging)
            {
                m_vm.autoOverlay.Clear();
                Int32Rect rect = new Int32Rect(0, 0, (int)m_vm.autoOverlay.Width, (int)m_vm.autoOverlay.Height);
            }

            m_dragging = false;
        }




        public Point ConvertToOverlayPosition(Point p, Image image, WriteableBitmap bitmap)
        {
            double windowSizeX = image.ActualWidth;
            double windowSizeY = image.ActualHeight;

            int x = (int)(p.X * bitmap.Width / windowSizeX);
            int y = (int)(p.Y * bitmap.Height / windowSizeY);

            return new Point((double)x, (double)y);
        }

        public void GetDrawPoints(out int x1, out int y1, out int x2, out int y2)
        {
            if (m_p2.X > m_p1.X)
            {
                x1 = (int)m_p1.X;
                x2 = (int)m_p2.X;
            }
            else
            {
                x1 = (int)m_p2.X;
                x2 = (int)m_p1.X;
            }

            if (m_p2.Y > m_p1.Y)
            {
                y1 = (int)m_p1.Y;
                y2 = (int)m_p2.Y;
            }
            else
            {
                y1 = (int)m_p2.Y;
                y2 = (int)m_p1.Y;
            }
        }


        private void PrevPB_Click(object sender, RoutedEventArgs e)
        {
            double timestamp;
            int width, height, depth;
            byte[] imageData;

            if(m_currentFrameIndex > 0)
            {
                int index = m_currentFrameIndex - 1;

                if(m_videoCache.GetFrame(index, out timestamp, out width, out height, out depth, out imageData))
                {
                    m_currentFrameIndex = index;
                    m_vm.SetManualImage(width, height, depth, imageData);
                    m_vm.timestamp = timestamp;

                    m_vm.redactions = m_editsDB.GetRedactionListForTimestamp(timestamp);
                    m_vm.RedrawRedactionBoxes_Manual();
                }
            }            
        }

        private void ForwPB_Click(object sender, RoutedEventArgs e)
        {
            double timestamp;
            int width, height, depth;
            byte[] imageData;

            if(m_currentFrameIndex < m_maxFrameIndex)
            {
                int index = m_currentFrameIndex + 1;

                if(m_videoCache.GetFrame(index, out timestamp, out width, out height, out depth, out imageData))
                {
                    m_currentFrameIndex = index;
                    m_vm.SetManualImage(width, height, depth, imageData);
                    m_vm.timestamp = timestamp;

                    m_vm.redactions = m_editsDB.GetRedactionListForTimestamp(timestamp);
                    m_vm.RedrawRedactionBoxes_Manual();
                }
            }
        }

        private void PrevFastPB_Click(object sender, RoutedEventArgs e)
        {
            double timestamp;
            int width, height, depth;
            byte[] imageData;

            int stepSize = 10;
            int index = m_currentFrameIndex - stepSize;
            if (index < 0) index = 0;

            if (index != m_currentFrameIndex)
            {
                if (m_videoCache.GetFrame(index, out timestamp, out width, out height, out depth, out imageData))
                {
                    m_currentFrameIndex = index;
                    m_vm.SetManualImage(width, height, depth, imageData);
                    m_vm.timestamp = timestamp;

                    m_vm.redactions = m_editsDB.GetRedactionListForTimestamp(timestamp);
                    m_vm.RedrawRedactionBoxes_Manual();
                }
            }
        }


        private void ForwFastPB_Click(object sender, RoutedEventArgs e)
        {
            double timestamp;
            int width, height, depth;
            byte[] imageData;

            int stepSize = 10;
            int index = m_currentFrameIndex + stepSize;
            if (index > m_maxFrameIndex) index = m_maxFrameIndex;

            if (index != m_currentFrameIndex)
            {
                if (m_videoCache.GetFrame(index, out timestamp, out width, out height, out depth, out imageData))
                {
                    m_currentFrameIndex = index;
                    m_vm.SetManualImage(width, height, depth, imageData);
                    m_vm.timestamp = timestamp;

                    m_vm.redactions = m_editsDB.GetRedactionListForTimestamp(timestamp);
                    m_vm.RedrawRedactionBoxes_Manual();
                }
            }
        }


        private void ManualVideoGrid_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                ForwPB_Click(null, null);
            else
                PrevPB_Click(null, null);
        }


        //////////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////
        //////////////////////////////////////////////////////////////////////////////

        // Test functions

        //public void LoadKeyFrames(string mp4Filename, Dictionary<int,double> gopDict, bool loadIntoListView)
        //{
        //    // All frames in MP4
        //    // KF1  F   F   KF2 F   F   KF3 F   F   KF4 F   F   KF5 F   F   KF6 F   F   KF7 F   F   KF8 F   F   KF9 F   F   KF10 F  F
        //    //
        //    // m_gopDictionaryAll:  all key frames, Dictionary<int,double>(index,timestamp)
        //    // KF1  KF2 KF3 KF4 KF5 KF6 KF7 KF8 KF9 KF10
        //    //
        //    // m_vm.gopList: key frames in scroll viewer, List<GOP_KEY_FRAME>
        //    // KF1 KF5 KF9

        //    const int MAX_SCROLLVIEWER_COUNT = 10;

        //    // get total number of key frames in file, so that we can figure out if we can load all of them.
        //    // If there's too many of them, we need to figure out how many to skip.

        //    // REPLACE THIS START
        //        int GOPsize = 10;
        //        string[] framesAll = Directory.GetFiles("d:\\temp1\\train", "*.jpg");               
        //        int totalFrames = framesAll.Length;

        //        // build list of ALL key frames
        //        gopDict.Clear();
        //        int ndx = 0;
        //        double timestamp = 0.0; // dummy timestamp
        //        foreach(string frame in framesAll)
        //        {
        //            if (ndx % GOPsize == 0)
        //                gopDict.Add(ndx, timestamp);
        //            ndx++;
        //            timestamp += 0.05;
        //        }

        //        int totalGOPs = gopDict.Count; // (totalFrames + GOPsize - 1) / GOPsize; // i.e. number of key frames
        //    // REPLACE THIS END


        //    // figure out skip count
        //    int skipCount = 1;
        //    while((totalGOPs/skipCount) > MAX_SCROLLVIEWER_COUNT)
        //    {
        //        skipCount++;
        //    }


        //    if (loadIntoListView)
        //    {
        //        // Get the appropriate key frames and load them into scroll viewer list
        //        m_vm.gopList.Clear();
        //        foreach (KeyValuePair<int, double> item in m_gopDictionary)
        //        {
        //            int index = item.Key;
        //            double tstamp = item.Value;
        //            if (index % skipCount == 0)
        //            {
        //                // REPLACE THIS START
        //                string filename = framesAll[index];
        //                byte[] arr;
        //                int width;
        //                int height;
        //                int depth;
        //                GetDecodedByteArray(filename, out width, out height, out depth, out arr);
        //                // REPLACE THIS END

        //                m_vm.gopList.Add(new GOP_KEY_FRAME(index, tstamp, width, height, depth, arr));
        //            }
        //        }
        //    }
            
        //}


        //public void LoadCache(GOPCache cache, string directory, string filePattern)
        //{
        //    cache.Reset();

        //    string[] files = Directory.GetFiles(directory, filePattern);

        //    double timestamp = 0.000;

        //    foreach(string filename in files)
        //    {                
        //        byte[] arr;
        //        int width;
        //        int height;
        //        int depth;

        //        GetDecodedByteArray(filename, out width, out height, out depth, out arr);

        //        cache.AddImageToCache(timestamp, width, height, 3, arr);

        //        timestamp += 0.05;
        //    }
        //}

        //public void LoadCache(int gopIndex)
        //{
        //    m_gopCache.Reset();

        //    double gop_timestamp = m_gopDictionary[gopIndex];
            
        //    // get all decoded frames in for gop with given timestamp, and then put them in the caches

        //    // REPLACE

        //    int GOPsize = 10;
        //    string[] files = Directory.GetFiles("d:\\temp1\\train", "*.jpg");            
            
        //    for(int i = gopIndex; i<gopIndex + GOPsize; i++)
        //    {
        //        if (i < files.Length)
        //        {
        //            byte[] arr;
        //            int width;
        //            int height;
        //            int depth;

        //            GetDecodedByteArray(files[i], out width, out height, out depth, out arr);

        //            m_gopCache.AddImageToCache(gop_timestamp, width, height, depth, arr);

        //            gop_timestamp += 0.05;
        //        }
        //        else
        //            break;
        //    }

        //    // END REPLACE

        //}

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


        public void LoadImageToManual(string filename)
        {
            System.Drawing.Image img = System.Drawing.Image.FromFile(filename);
            byte[] arr = ImageToByteArray(img);
            int width = img.Width;
            int height = img.Height;            

            m_vm.SetManualImage(width, height, 3, arr);
        }


        public byte[] ImageToByteArray(System.Drawing.Image imageIn)
        {
            using (var ms = new MemoryStream())
            {
                imageIn.Save(ms, imageIn.RawFormat);
                return ms.ToArray();
            }
        }

        private void Update_Manual(double timestamp, int width, int height, int depth, byte[] data)
        {
            m_vm.SetManualImage(width, height, depth, data);
            m_vm.timestamp = timestamp;
        }

        private void Update_Auto(double timestamp, int width, int height, int depth, byte[] data)
        {
            m_vm.SetAutoImage(width, height, depth, data);
            m_vm.timestamp = timestamp;
        }

        private void MainRibbon_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MainTabControl.SelectedIndex = m_vm.activeTabIndex;
        }

        private void GOP_ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //m_currentGOPindex = m_vm.selectedGOP.index;
            //double gopTimestamp = m_vm.selectedGOP.timestamp;

            //Update(m_currentGOPindex, false);
        }



        ////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////

        #region Video Player


        public void Player_Start(string filename, int decodeWidth, int decodeHeight, bool paceOutput)
        {
            VideoTools.Mp4Reader mp4Reader = new VideoTools.Mp4Reader();
            m_cancelTokenSource = new CancellationTokenSource();
            m_pauseTokenSource = new WPFTools.PauseTokenSource();
            m_pauseTokenSource.IsPaused = false;
            mp4Reader.StartPlayback(filename, NewFrame_to_Player, decodeWidth, decodeHeight,
                m_cancelTokenSource, m_pauseTokenSource, paceOutput);            
        }


        public void NewFrame_to_Player(VideoTools.ProgressStruct frame)
        {
            if (frame.timestamp == -1)
            {
                Player_StopPB_Click(null, null);
            }
            else
            {
                // handle new frame coming in
                BitmapSource bs = BitmapSource.Create(frame.width, frame.height, 96, 96,
                                PixelFormats.Bgr24, null, frame.data, frame.width * 3);

                m_vm.playerBitmap = new WriteableBitmap(bs);

                double percentDone = (double)frame.timestamp / (double)frame.length * 100.0f;
                videoNavigator.CurrentValue = percentDone;
            }
        }


        private void Player_PlayPB_Click(object sender, RoutedEventArgs e)
        {
            switch(m_vm.state)
            {              
                case AppState.PLAYER_PAUSED:
                    m_pauseTokenSource.IsPaused = false;

                    PlayerRibbon_PlayPB.IsEnabled = false;
                    PlayerRibbon_PausePB.IsEnabled = true;
                    PlayerRibbon_StopPB.IsEnabled = true;

                    m_vm.state = AppState.PLAYER_PLAYING;
                    break;
                case AppState.READY: // player stopped
                    if (m_vm.mp4Filename != null)
                        if (File.Exists(m_vm.mp4Filename))
                        {
                            Player_Start(m_vm.mp4Filename, 640, 480, true);

                            PlayerRibbon_PlayPB.IsEnabled = false;
                            PlayerRibbon_PausePB.IsEnabled = true;
                            PlayerRibbon_StopPB.IsEnabled = true;

                            m_vm.state = AppState.PLAYER_PLAYING;
                            m_pauseTokenSource.IsPaused = false;

                        }
                    break;
            }

            
        }

        private void Player_PausePB_Click(object sender, RoutedEventArgs e)
        {
            switch (m_vm.state)
            { 
                case AppState.PLAYER_PAUSED:
                    m_pauseTokenSource.IsPaused = false;

                    PlayerRibbon_PlayPB.IsEnabled = false;
                    PlayerRibbon_PausePB.IsEnabled = true;
                    PlayerRibbon_StopPB.IsEnabled = true;

                    m_vm.state = AppState.PLAYER_PLAYING;
                    break;
                case AppState.PLAYER_PLAYING:
                    m_pauseTokenSource.IsPaused = true;

                    PlayerRibbon_PlayPB.IsEnabled = true;
                    PlayerRibbon_PausePB.IsEnabled = false;
                    PlayerRibbon_StopPB.IsEnabled = true;

                    m_vm.state = AppState.PLAYER_PAUSED;
                    break; 
            }
        }

        private void RedactionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            m_vm.RedrawRedactionBoxes_Manual();
        }

     

        private void Player_StopPB_Click(object sender, RoutedEventArgs e)
        {
            m_pauseTokenSource.IsPaused = false;
            m_cancelTokenSource.Cancel();

            PlayerRibbon_PlayPB.IsEnabled = true;
            PlayerRibbon_PausePB.IsEnabled = false;
            PlayerRibbon_StopPB.IsEnabled = false;

            m_vm.state = AppState.READY;

            m_vm.playerBitmap.Clear();

            videoNavigator.CurrentValue = 0;
        }





        public void Redaction_Start(string filename, int decodeWidth, int decodeHeight, bool paceOutput)
        {
            VideoTools.Mp4Reader mp4Reader = new VideoTools.Mp4Reader();
            m_cancelTokenSource = new CancellationTokenSource();
            m_pauseTokenSource = new WPFTools.PauseTokenSource();
            m_pauseTokenSource.IsPaused = false;
            mp4Reader.StartPlayback(filename, NewFrame_to_Redact, decodeWidth, decodeHeight,
                m_cancelTokenSource, m_pauseTokenSource, paceOutput);
        }



        public void NewFrame_to_Redact(VideoTools.ProgressStruct frame)
        {
            if (frame.timestamp == -1)
            {
                AutoRedactRibbon_StopPB_Click(null, null);
            }
            else
            {
                // handle new frame coming in
                //BitmapSource bs = BitmapSource.Create(frame.width, frame.height, 96, 96,
                //                PixelFormats.Bgr24, null, frame.data, frame.width * 3);

                //m_vm.autoImage = new WriteableBitmap(bs);

                double percentDone = (double)frame.timestamp / (double)frame.length * 100.0f;
                videoNavigator.CurrentValue = percentDone;


                // submit frame to DNN

                if(m_vm.autoImage.PixelWidth != frame.width || m_vm.autoImage.PixelHeight != frame.height)
                {
                    m_vm.autoImage = BitmapFactory.New(frame.width, frame.height);
                }
                m_pipeline.Post(Tuple.Create<byte[], double, int, int, int, WriteableBitmap, bool>(frame.data, frame.timestamp, frame.width, frame.height, 3,
                                                                                           m_vm.autoImage, false));
            }
        }


        private void AutoRedactRibbon_PlayPB_Click(object sender, RoutedEventArgs e)
        {
            switch (m_vm.state)
            {
                case AppState.REDACTION_PAUSED:
                    m_pauseTokenSource.IsPaused = false;

                    AutoRedactRibbon_PlayPB.IsEnabled = false;
                    AutoRedactRibbon_PausePB.IsEnabled = true;
                    AutoRedactRibbon_StopPB.IsEnabled = true;

                    m_vm.state = AppState.REDACTION_RUNNING;
                    break;
                case AppState.READY: // player stopped
                    if (m_vm.mp4Filename != null)
                        if (File.Exists(m_vm.mp4Filename))
                        {
                            Redaction_Start(m_vm.mp4Filename, 640, 480, false);

                            AutoRedactRibbon_PlayPB.IsEnabled = false;
                            AutoRedactRibbon_PausePB.IsEnabled = true;
                            AutoRedactRibbon_StopPB.IsEnabled = true;

                            m_vm.state = AppState.REDACTION_RUNNING;
                            m_pauseTokenSource.IsPaused = false;
                        }
                    break;
            }
        }

        private void AutoRedactRibbon_PausePB_Click(object sender, RoutedEventArgs e)
        {
            switch (m_vm.state)
            {
                case AppState.REDACTION_PAUSED:
                    m_pauseTokenSource.IsPaused = false;

                    AutoRedactRibbon_PlayPB.IsEnabled = false;
                    AutoRedactRibbon_PausePB.IsEnabled = true;
                    AutoRedactRibbon_StopPB.IsEnabled = true;

                    m_vm.state = AppState.REDACTION_RUNNING;
                    break;
                case AppState.REDACTION_RUNNING:
                    m_pauseTokenSource.IsPaused = true;

                    AutoRedactRibbon_PlayPB.IsEnabled = true;
                    AutoRedactRibbon_PausePB.IsEnabled = false;
                    AutoRedactRibbon_StopPB.IsEnabled = true;

                    m_vm.state = AppState.REDACTION_PAUSED;
                    break;
            }
        }

        private void AutoRedactRibbon_StopPB_Click(object sender, RoutedEventArgs e)
        {
            m_pauseTokenSource.IsPaused = false;
            m_cancelTokenSource.Cancel();

            AutoRedactRibbon_PlayPB.IsEnabled = true;
            AutoRedactRibbon_PausePB.IsEnabled = false;
            AutoRedactRibbon_StopPB.IsEnabled = false;

            m_vm.state = AppState.READY;

            m_vm.autoImage.Clear();
            m_vm.autoOverlay.Clear();

            videoNavigator.CurrentValue = 0;
        }


        private void TrackPB_Click(object sender, RoutedEventArgs e)
        {

        }


        #endregion



    }


    public enum AppState
    {
        MP4_NOT_SET,
        READY,        
        PLAYER_PAUSED,
        PLAYER_PLAYING,
        REDACTION_PAUSED,
        REDACTION_RUNNING
    }



    public class MainViewModel : INotifyPropertyChanged
    {
        private AppState _state;
        public AppState state
        {
            get { return _state; }
            set
            {
                _state = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("state"));
            }
        }


        private int _activeTabIndex;
        public int activeTabIndex
        {
            get { return _activeTabIndex; }
            set
            {
                _activeTabIndex = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("activeTabIndex"));
                if (_activeTabIndex != 2) trackingEnabled = false;  // disable tracking if ribbon tab changes away from Manual
            }
        }


        private string _mp4Filename;
        public string mp4Filename
        {
            get { return _mp4Filename; }
            set
            {
                _mp4Filename = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("mp4Filename"));
            }
        }

        private int _width;
        public int width
        {
            get { return _width; }
            set
            {
                _width = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("width"));
            }
        }

        private int _height;
        public int height
        {
            get { return _height; }
            set
            {
                _height = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("height"));
            }
        }


        private int _depth;
        public int depth
        {
            get { return _depth; }
            set
            {
                _depth = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("depth"));
            }
        }


        private double _timestamp;
        public double timestamp
        {
            get { return _timestamp; }
            set
            {
                _timestamp = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("timestamp"));
                timestampStr = String.Format("{0:0.000}", value);
            }
        }


        private string _timestampStr;
        public string timestampStr
        {
            get { return _timestampStr; }
            set
            {
                _timestampStr = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("timestampStr"));
            }
        }


        private int _numRedactions;
        public int numRedactions
        {
            get { return _numRedactions; }
            set
            {
                _numRedactions = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("numRedactions"));
            }
        }



        private WriteableBitmap _autoImage;
        public WriteableBitmap autoImage
        {
            get { return _autoImage; }
            set
            {
                _autoImage = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("autoImage"));
            }
        }

        private WriteableBitmap _autoOverlay;
        public WriteableBitmap autoOverlay
        {
            get { return _autoOverlay; }
            set
            {
                _autoOverlay = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("autoOverlay"));
            }
        }


        private WriteableBitmap _manualImage;
        public WriteableBitmap manualImage
        {
            get { return _manualImage; }
            set { _manualImage = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("manualImage")); }
        }

        private WriteableBitmap _manualOverlay;
        public WriteableBitmap manualOverlay
        {
            get { return _manualOverlay; }
            set
            { _manualOverlay = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("manualOverlay"));
            }
        }

        private ObservableCollection<GOP_KEY_FRAME> _gopList;
        public ObservableCollection<GOP_KEY_FRAME> gopList
        {
            get { return _gopList; }
            set
            {
                _gopList = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("gopList"));
            }
        }

        private GOP_KEY_FRAME _selectedGOP;
        public GOP_KEY_FRAME selectedGOP
        {
            get { return _selectedGOP; }
            set
            {
                _selectedGOP = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("selectedGOP"));
            }
        }

        private bool _trackingEnabled;
        public bool trackingEnabled
        {
            get { return _trackingEnabled; }
            set { _trackingEnabled = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("trackingEnabled")); }
        }

        private ObservableCollection<FrameEdit> _redactions;
        public ObservableCollection<FrameEdit> redactions
        {
            get { return _redactions; }
            set { _redactions = value; PropertyChanged(this, new PropertyChangedEventArgs("redactions")); }
        }


        private FrameEdit _selectedRedaction;
        public FrameEdit selectedRedaction
        {
            get { return _selectedRedaction; }
            set { _selectedRedaction = value; PropertyChanged(this, new PropertyChangedEventArgs("selectedRedaction")); }
        }

        private ICommand _deleteRedactionCommand { get; set; }
        public ICommand deleteRedactionCommand
        {
            get
            {
                return _deleteRedactionCommand;
            }
            set
            {
                _deleteRedactionCommand = value;
            }
        }

        public void DeleteSelectedRedaction(object obj)
        {
            redactions.Remove(selectedRedaction);
            RedrawRedactionBoxes_Manual();
            RedrawRedactionBoxes_Auto();
        }

        public void DeleteAnnotation(FrameEdit redaction)
        {
            redactions.Remove(redaction);
            RedrawRedactionBoxes_Manual();
            RedrawRedactionBoxes_Auto();
        }

        private Color _fillColor;
        public Color fillColor
        {
            get { return _fillColor; }
            set { _fillColor = value; PropertyChanged(this, new PropertyChangedEventArgs("fillColor")); }
        }

        private Color _selectedFillColor;
        public Color selectedFillColor
        {
            get { return _selectedFillColor; }
            set { _selectedFillColor = value; PropertyChanged(this, new PropertyChangedEventArgs("selectedFillColor")); }
        }


        public void RedrawRedactionBoxes_Manual()
        {
            manualOverlay.Clear();

            foreach (FrameEdit fe in redactions)
            {
                if(fe == selectedRedaction)
                    manualOverlay.FillRectangle(fe.box.x1, fe.box.y1, fe.box.x2, fe.box.y2, selectedFillColor);
                else
                    manualOverlay.FillRectangle(fe.box.x1, fe.box.y1, fe.box.x2, fe.box.y2, fillColor);
            }
        }



        public void RedrawRedactionBoxes_Auto()
        {
            autoOverlay.Clear();

            foreach (FrameEdit fe in redactions)
            {               
                if (fe == selectedRedaction)
                    autoOverlay.FillRectangle(fe.box.x1, fe.box.y1, fe.box.x2, fe.box.y2, selectedFillColor);
                else
                    autoOverlay.FillRectangle(fe.box.x1, fe.box.y1, fe.box.x2, fe.box.y2, fillColor);
            }
        }



        public void SetManualImage(int Width, int Height, int Depth, byte[] data)
        {   
            if(Width != width || Height != height)
            {
                width = Width;
                height = Height;
                depth = Depth;
                PixelFormat pf = PixelFormats.Bgr24;
                if (depth > 3) pf = PixelFormats.Bgra32;
                manualImage = new WriteableBitmap(width, height, 96, 96, pf, null);                
                manualOverlay = BitmapFactory.New(width, height);
            }

            manualOverlay.Clear();
            Int32Rect rect = new Int32Rect(0, 0, width, height);
            manualImage.Lock();
            manualImage.WritePixels(rect, data, width * depth, 0);
            manualImage.Unlock();
        }



        public void SetAutoImage(int Width, int Height, int Depth, byte[] data)
        {
            if (autoImage != null)
            {
                if (Width != autoImage.PixelWidth || Height != autoImage.PixelHeight)
                {
                    PixelFormat pf = PixelFormats.Bgr24;
                    if (depth > 3) pf = PixelFormats.Bgra32;
                    autoImage = new WriteableBitmap(width, height, 96, 96, pf, null);
                    autoOverlay = BitmapFactory.New(width, height);
                }
            }
            else
            {
                PixelFormat pf = PixelFormats.Bgr24;
                if (depth > 3) pf = PixelFormats.Bgra32;
                autoImage = new WriteableBitmap(width, height, 96, 96, pf, null);
                autoOverlay = BitmapFactory.New(width, height);
            }

            autoOverlay.Clear();
            Int32Rect rect = new Int32Rect(0, 0, autoImage.PixelWidth, autoImage.PixelHeight);
            autoImage.Lock();
            autoImage.WritePixels(rect, data, autoImage.PixelWidth * depth, 0);
            autoImage.Unlock();
        }


        //////////////////////////////////////////////////////////////////////////////////////////////////
        // player members


        private WriteableBitmap _playerBitmap;
        public WriteableBitmap playerBitmap
        {
            get { return _playerBitmap; }
            set { _playerBitmap = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("playerBitmap"));
            }
        }



        public MainViewModel()
        {
            width = 0;
            height = 0;
            
            timestamp = 0.0;
            
            _gopList = new ObservableCollection<GOP_KEY_FRAME>();

            state = AppState.MP4_NOT_SET;

            _deleteRedactionCommand = new WPFTools.RelayCommand(DeleteSelectedRedaction);

            _fillColor = Color.FromArgb(0x55, 0xff, 0x00, 0x00);

            _selectedFillColor = Color.FromArgb(0x55, 0xf9, 0xff, 0x33);


        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    public class GOP_KEY_FRAME : INotifyPropertyChanged
    {
        private int _index;
        public int index
        {
            get { return _index; }
            set
            {
                _index = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("index"));                
            }
        }

        private double _timestamp;
        public double timestamp
        {
            get { return _timestamp; }
            set
            {
                _timestamp = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("timestamp"));
                timestampStr = String.Format("{0:0.000}", value);
            }
        }

        private string _timestampStr;
        public string timestampStr
        {
            get { return _timestampStr; }
            private set
            {
                _timestampStr = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("timestampStr"));
            }
        }

        private WriteableBitmap _bitmap;
        public WriteableBitmap bitmap
        {
            get { return _bitmap; }
            set
            {
                _bitmap = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("bitmap"));
            }
        }

        public GOP_KEY_FRAME(int Index, double Timestamp, int width, int height, int depth, byte[] thumbnailData)
        {
            index = Index;
            timestamp = Timestamp;
            PixelFormat pf = PixelFormats.Bgr24;
            if (depth > 3) pf = PixelFormats.Bgra32;
            bitmap = new WriteableBitmap(width, height, 96, 96, pf, null);
            Int32Rect rect = new Int32Rect(0, 0, width, height);
            bitmap.Lock();
            bitmap.WritePixels(rect, thumbnailData, width * depth, 0);            
            bitmap.Unlock();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }



}
