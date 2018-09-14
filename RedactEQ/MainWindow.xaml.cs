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
using Equature.Integration;

namespace RedactEQ
{
    
   

    public partial class MainWindow : Window
    {
        string m_errorMsg;
  
        VideoCache m_videoCache;                                    

        MainViewModel m_vm;

        NonMaximumSuppression m_nms;

        private DNNengine m_engine;
        CancellationTokenSource m_cancelTokenSource;
        WPFTools.PauseTokenSource m_pauseTokenSource;
        TaskScheduler m_uiTask;
        int m_analysisWidth, m_analysisHeight;

        ITargetBlock<Tuple<ImagePackage, WriteableBitmap, WriteableBitmap, bool>> m_pipeline;

        VideoEditsDatabase m_editsDB;
        //int m_currentFrameIndex;
        int m_maxFrameIndex;
        bool m_dragging;
        Point m_p1, m_p2;

        ITargetBlock<Tuple<int>> m_cachePipeline;

        bool m_waitingForTrackingRect = false;
        bool m_tracking = false;
        Int32Rect m_startingTrackingRect;
        CVTracker m_tracker;

        int m_manualAutoStep;

        long m_currentMp4FileDurationMilliseconds = 0;

        public MainWindow()
        {
            InitializeComponent();
            m_vm = new MainViewModel();
            DataContext = m_vm;
            m_vm.currentFrameIndex = 0;

            m_analysisHeight = 480;
            m_analysisWidth = 640;

            m_manualAutoStep = 0;
            
            m_uiTask = TaskScheduler.FromCurrentSynchronizationContext();

            TrackStepForwPB.Visibility = Visibility.Hidden;
            TrackRunForwPB.Visibility = Visibility.Hidden;

            videoNavigator.RangeChanged += VideoNavigator_RangeChanged;

        }

        private void VideoNavigator_RangeChanged(object sender, RangeSliderEventArgs e)
        {
            double targetTimestamp = e.Current / 100.0 * (double)m_currentMp4FileDurationMilliseconds / 1000.0;
            int frameIndex;
            double timestamp;
            double percentPosition;
            if(m_videoCache.GetClosestFrameIndex(targetTimestamp, out frameIndex, out timestamp, out percentPosition))
            {
                m_cachePipeline.Post(Tuple.Create<int>(frameIndex));
                videoNavigator.CurrentValue = percentPosition;
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

                    m_nms = new NonMaximumSuppression();
                    m_nms.Init();

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

                // create new video edits database and bind to listview

                if (m_editsDB != null) m_editsDB.SaveDatabase();

                m_editsDB = new VideoEditsDatabase(m_vm.mp4Filename);


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


                m_videoCache = new VideoCache("VideoCache", m_vm.mp4Filename, 2);
                m_videoCache.Init(640, 480);

                m_videoCache.VideoCacheEvent += M_videoCache_VideoCacheEvent;

                m_cachePipeline = m_videoCache.CreateCacheUpdatePipeline(2);

                m_maxFrameIndex = m_videoCache.GetMaxFrameIndex();

                m_currentMp4FileDurationMilliseconds = m_videoCache.GetVideoDuration();

                m_videoCache.GetGopList(m_vm.mp4Filename, null);

                PlayerRibbon_PlayPB.IsEnabled = true;
                AutoRedact_PlayPB.IsEnabled = true;

                m_vm.state = AppState.READY;

            }
        }

        private void M_videoCache_VideoCacheEvent(object sender, VideoCache_EventArgs e)
        {
            switch(e.status)
            {
                case VideoCache_Status_Type.ERROR:
                    // run on UI thread
                    Application.Current.Dispatcher.Invoke(new Action(() => {
                        MessageBox.Show(e.message, "Video Frame Cache Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }));                    
                    break;
                case VideoCache_Status_Type.FRAME:
                    if(e.frame != null)
                    {
                        // run on UI thread
                        Application.Current.Dispatcher.Invoke(new Action(() => {
                            m_vm.currentFrameIndex = e.frame.frameIndex;

                            switch(m_vm.activeTabIndex)
                            {
                                case 0: // Auto
                                    m_vm.SetAutoImage(e.frame.imagePackage.width, e.frame.imagePackage.height, e.frame.imagePackage.numChannels, e.frame.imagePackage.data);
                                    goto case 1;                                    
                                case 1: // Manual
                                    m_vm.SetManualImage(e.frame.imagePackage.width, e.frame.imagePackage.height, e.frame.imagePackage.numChannels, e.frame.imagePackage.data);
                                    m_vm.lastManualImageByteArray = e.frame.imagePackage.data;
                                    if(m_tracking)
                                    {
                                        UpdateTracker(e.frame.timestamp);

                                        if (m_manualAutoStep > 0)
                                            ForwPB_Click(null, null);
                                        else if (m_manualAutoStep < 0)
                                            PrevPB_Click(null, null);
                                    }
                                    break;
                                case 2: // Player
                                    break;
                            }                            

                            m_vm.timestamp = e.frame.timestamp;

                            m_vm.redactions = m_editsDB.GetRedactionListForTimestamp(e.frame.timestamp);
                            m_vm.RedrawRedactionBoxes_Manual();

                            double durationOfEntireVideo = (double)m_videoCache.GetVideoDuration() / 1000.0;
                            double percentDone = e.frame.timestamp / durationOfEntireVideo * 100.0f;
                            videoNavigator.CurrentValue = percentDone;
                        }));
                    }
                    break;
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

            if(m_waitingForTrackingRect)
            {
                m_startingTrackingRect = new Int32Rect(x1, y1, x2 - x1 + 1, y2 - y1 + 1);                

                // Create a tracker
                m_tracker = new CVTracker();
                m_tracker.Init(TrackerType.KCF);
                if (m_vm.lastManualImageByteArray != null)
                {
                    if(m_tracker.StartTracking(m_vm.lastManualImageByteArray, m_vm.width, m_vm.height,
                        m_startingTrackingRect.X, m_startingTrackingRect.Y, m_startingTrackingRect.Width, m_startingTrackingRect.Height))
                    {
                        m_tracking = true;
                        m_vm.manualMessage = "Ready to Track";
                        TrackStepForwPB.Visibility = Visibility.Visible;
                        TrackRunForwPB.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        MessageBox.Show("Failed to Initialize Tracker", "Tracker Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        m_tracking = false;
                        m_vm.manualMessage = "";

                    }
                }


                if (m_editsDB != null)
                {
                    if (m_tracking)
                        m_editsDB.AddFrameEdit(m_vm.timestamp, FRAME_EDIT_TYPE.TRACKING_REDACTION, new VideoTools.BoundingBox(x1, y1, x2, y2));
                    else
                        m_editsDB.AddFrameEdit(m_vm.timestamp, FRAME_EDIT_TYPE.MANUAL_REDACTION, new VideoTools.BoundingBox(x1, y1, x2, y2));
                }

                m_vm.RedrawRedactionBoxes_Manual();
            }
                
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

            //if (m_editsDB != null)
            //    m_editsDB.AddFrameEdit(m_vm.timestamp, FRAME_EDIT_TYPE.REDACTION, new VideoTools.BoundingBox(x1, y1, x2, y2));
            
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
            if(m_vm.currentFrameIndex > 0)
            {
                int index = m_vm.currentFrameIndex - 1;

                m_cachePipeline.Post(Tuple.Create<int>(index));
            }            
        }

        private void ForwPB_Click(object sender, RoutedEventArgs e)
        {
            if(m_vm.currentFrameIndex < m_maxFrameIndex)
            {
                int index = m_vm.currentFrameIndex + 1;

                m_cachePipeline.Post(Tuple.Create<int>(index));
            }
        }

        private void PrevFastPB_Click(object sender, RoutedEventArgs e)
        {
            int stepSize = 10;
            int index = m_vm.currentFrameIndex - stepSize;
            if (index < 0) index = 0;

            if (index != m_vm.currentFrameIndex)
            {
                m_cachePipeline.Post(Tuple.Create<int>(index));
            }
        }


        private void ForwFastPB_Click(object sender, RoutedEventArgs e)
        {
            int stepSize = 10;
            int index = m_vm.currentFrameIndex + stepSize;
            if (index > m_maxFrameIndex) index = m_maxFrameIndex;

            if (index != m_vm.currentFrameIndex)
            {
                m_cachePipeline.Post(Tuple.Create<int>(index));
            }
        }


        private void ManualVideoGrid_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                ForwPB_Click(null, null);
            else
                PrevPB_Click(null, null);
        }


     

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


        public bool LoadImageFromFile(string filename, ref int width, ref int height, ref int depth, ref byte[] data)
        {
            //System.Drawing.Image img = System.Drawing.Image.FromFile(filename);
            //byte[] arr = ImageToByteArray(img);
            //width = img.Width;
            //height = img.Height;
            //System.Drawing.Imaging.PixelFormat format = img.PixelFormat;
            //if(format == System.Drawing.Imaging.PixelFormat.)

            bool success = true;
            int w, h, d;
            byte[] arr;
            if (GetDecodedByteArray(filename, out w, out h, out d, out arr))
            {
                width = w;
                height = h;
                depth = d;
                data = arr;                
            }
            else
            {
                success = false;
            }

            return success;
            
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

                double durationOfEntireVideo = (double)m_videoCache.GetVideoDuration() / 1000.0;
                double percentDone = frame.timestamp / durationOfEntireVideo * 100.0f;                
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





        public void Redaction_Start(string filename, int decodeWidth, int decodeHeight, bool paceOutput, float confidence, bool useTracker)
        {
            VideoTools.Mp4Reader mp4Reader = new VideoTools.Mp4Reader();
            m_cancelTokenSource = new CancellationTokenSource();
            m_pauseTokenSource = new WPFTools.PauseTokenSource();
            m_pauseTokenSource.IsPaused = false;

            IntPtr reader = Mp4.CreateMp4Reader(filename);
            long durationMilliseconds;
            double frameRate;
            int width, height, sampleCount;
            if(Mp4.GetVideoProperties(reader, out durationMilliseconds, out frameRate, out width, out height, out sampleCount))
            {
                double startTimestamp = videoNavigator.LowerValue / 100.0 * (double)(durationMilliseconds) / 1000.0;
                double endTimestamp = videoNavigator.UpperValue / 100.0 * (double)(durationMilliseconds) / 1000.0;
                mp4Reader.StartPlayback_1(filename, NewFrame_to_Redact, decodeWidth, decodeHeight, startTimestamp, endTimestamp, m_engine, confidence, useTracker,
                    m_cancelTokenSource, m_pauseTokenSource, paceOutput);
            }
           
        }



        public void NewFrame_to_Redact(VideoTools.ProgressStruct frame)
        {
            if (frame.timestamp == -1)
            {
                AutoRedact_StopPB_Click(null, null);
            }
            else
            { 

                // run on UI thread
                Application.Current.Dispatcher.Invoke(new Action(() => {
                                        
                    BitmapSource bs = BitmapSource.Create(frame.width, frame.height, 96, 96,
                                    PixelFormats.Bgr24, null, frame.data, frame.width * 3);

                    m_vm.autoImage = new WriteableBitmap(bs);

                    double durationOfEntireVideo = (double)m_videoCache.GetVideoDuration() / 1000.0;
                    double percentDone = frame.timestamp / durationOfEntireVideo * 100.0f;                    
                    videoNavigator.CurrentValue = percentDone;

                    if(frame.boxList != null)
                    {
                        frame.boxList = m_nms.Execute(frame.boxList, m_editsDB.GetBoundingBoxesForTimestamp(m_vm.timestamp, frame.width, frame.height), 0.60f);

                        m_editsDB.AddRedactionBoxesFromDNN(frame.boxList, frame.timestamp, frame.width, frame.height);
                        
                        m_vm.redactions = m_editsDB.GetEditsForFrame(frame.timestamp);
                        m_vm.RedrawRedactionBoxes_Auto();
                    }
                    
                }));

            }
        }


        private void AutoRedact_PlayPB_Click(object sender, RoutedEventArgs e)
        {
            switch (m_vm.state)
            {
                case AppState.REDACTION_PAUSED:
                    m_pauseTokenSource.IsPaused = false;

                    AutoRedact_PlayPB.IsEnabled = false;
                    AutoRedact_PausePB.IsEnabled = true;
                    AutoRedact_StopPB.IsEnabled = true;

                    m_vm.state = AppState.REDACTION_RUNNING;
                    break;
                case AppState.READY: // player stopped
                    if (m_vm.mp4Filename != null)
                        if (File.Exists(m_vm.mp4Filename))
                        {
                            Redaction_Start(m_vm.mp4Filename, 640, 480, false, 0.70f, false);

                            AutoRedact_PlayPB.IsEnabled = false;
                            AutoRedact_PausePB.IsEnabled = true;
                            AutoRedact_StopPB.IsEnabled = true;

                            m_vm.state = AppState.REDACTION_RUNNING;
                            m_pauseTokenSource.IsPaused = false;
                        }
                    break;
            } 
        }



        private void AutoRedact_PausePB_Click(object sender, RoutedEventArgs e)
        {
            switch (m_vm.state)
            {
                case AppState.REDACTION_PAUSED:
                    m_pauseTokenSource.IsPaused = false;

                    AutoRedact_PlayPB.IsEnabled = false;
                    AutoRedact_PausePB.IsEnabled = true;
                    AutoRedact_StopPB.IsEnabled = true;

                    m_vm.state = AppState.REDACTION_RUNNING;
                    break;
                case AppState.REDACTION_RUNNING:
                    m_pauseTokenSource.IsPaused = true;

                    AutoRedact_PlayPB.IsEnabled = true;
                    AutoRedact_PausePB.IsEnabled = false;
                    AutoRedact_StopPB.IsEnabled = true;

                    m_vm.state = AppState.REDACTION_PAUSED;
                    break;
            }
        }


 
        private void TrackPB_Checked(object sender, RoutedEventArgs e)
        {
            m_waitingForTrackingRect = true;
            m_tracking = false;
            m_startingTrackingRect = new Int32Rect(0,0,0,0);

            NavigateGroupBox.Visibility = Visibility.Hidden;
            //TrackStepForwPB.Visibility = Visibility.Visible;
            //TrackRunForwPB.Visibility = Visibility.Visible;

            m_vm.manualMessage = "Select Area To Track";
        }

        private void TrackPB_Unchecked(object sender, RoutedEventArgs e)
        {
            m_waitingForTrackingRect = false;
            m_tracking = false;

            NavigateGroupBox.Visibility = Visibility.Visible;
            TrackStepForwPB.Visibility = Visibility.Hidden;
            TrackRunForwPB.Visibility = Visibility.Hidden;

            m_vm.manualMessage = "";
        }

    

        private void TrackStepForwPB_Click(object sender, RoutedEventArgs e)
        {
            m_manualAutoStep = 0;
            ForwPB_Click(null, null);           
        }

        private void TrackRunForwPB_Click(object sender, RoutedEventArgs e)
        {
            m_manualAutoStep = 1;
            ForwPB_Click(null, null);
        }

        private void TestPB_Click(object sender, RoutedEventArgs e)
        {
            int method = 4;  // 1 = from memory buffer, 2 = from file, 3 = from current Manual image 
            List<DNNTools.BoundingBox> boxList;
            NonMaximumSuppression nms = new NonMaximumSuppression();
            int w = 0, h = 0, d = 0;
            byte[] data = null;

            switch (method)
            {
                case 1:
                        OpenFileDialog openFileDialog1 = new OpenFileDialog();

                        //openFileDialog1.InitialDirectory = @"d:\";
                        openFileDialog1.Title = "Browse Image Files";

                        openFileDialog1.CheckFileExists = true;
                        openFileDialog1.CheckPathExists = true;

                        openFileDialog1.DefaultExt = "jpg";
                        openFileDialog1.Filter = "JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png";
                        openFileDialog1.FilterIndex = 1;
                        openFileDialog1.RestoreDirectory = true;

                        openFileDialog1.ReadOnlyChecked = true;
                        openFileDialog1.ShowReadOnly = true;

                        Nullable<bool> result = openFileDialog1.ShowDialog();
                        if (result == true)
                        {
                            string filename = openFileDialog1.FileName;

                            
                            if (LoadImageFromFile(filename, ref w, ref h, ref d, ref data))
                            {

                                boxList = m_engine.EvalImage(data, w, h, d, w, h, 0.70f);
                            
                                boxList = nms.Execute(boxList, 0.50f);


                                m_vm.SetManualImage(w, h, d, data);
                                m_vm.manualOverlay.Clear();
                                foreach (DNNTools.BoundingBox box in boxList)
                                {
                                    int x1 = (int)(box.x1 * (float)w);
                                    int y1 = (int)(box.y1 * (float)h);
                                    int x2 = (int)(box.x2 * (float)w);
                                    int y2 = (int)(box.y2 * (float)h);
                                    m_vm.manualOverlay.FillRectangle(x1, y1, x2, y2, m_vm.fillColor);
                                }

                                m_vm.manualMessage = boxList.Count.ToString();
                            }
                        }
                    break;
                case 2:

                    OpenFileDialog openFileDialog2 = new OpenFileDialog();

                    //openFileDialog1.InitialDirectory = @"d:\";
                    openFileDialog2.Title = "Browse Image Files";

                    openFileDialog2.CheckFileExists = true;
                    openFileDialog2.CheckPathExists = true;

                    openFileDialog2.DefaultExt = "jpg";
                    openFileDialog2.Filter = "JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png";
                    openFileDialog2.FilterIndex = 1;
                    openFileDialog2.RestoreDirectory = true;

                    openFileDialog2.ReadOnlyChecked = true;
                    openFileDialog2.ShowReadOnly = true;

                    Nullable<bool> result2 = openFileDialog2.ShowDialog();
                    if (result2 == true)
                    {
                        string filename = openFileDialog2.FileName;

                        if (LoadImageFromFile(filename, ref w, ref h, ref d, ref data))
                        {   
                            boxList = m_engine.EvalImageFile(filename, w, h, 0.70f);

                            boxList = nms.Execute(boxList, 0.50f);


                            m_vm.SetManualImage(w, h, d, data);
                            m_vm.manualOverlay.Clear();
                            foreach (DNNTools.BoundingBox box in boxList)
                            {
                                int x1 = (int)(box.x1 * (float)w);
                                int y1 = (int)(box.y1 * (float)h);
                                int x2 = (int)(box.x2 * (float)w);
                                int y2 = (int)(box.y2 * (float)h);
                                m_vm.manualOverlay.FillRectangle(x1, y1, x2, y2, m_vm.fillColor);
                            }

                            m_vm.manualMessage = boxList.Count.ToString();
                        }
                    }
                    break;

                case 3:

                    w = m_vm.width;
                    h = m_vm.height;
                    d = m_vm.depth;
                    
                    boxList = m_engine.EvalImage(m_vm.lastManualImageByteArray, w, h, d, w, h, 0.70f);

                    boxList = nms.Execute(boxList, m_editsDB.GetBoundingBoxesForTimestamp(m_vm.timestamp,w,h), 0.70f);                    

                    m_editsDB.AddRedactionBoxesFromDNN(boxList, m_vm.timestamp, w, h);

                    m_vm.RedrawRedactionBoxes_Manual();

                    break;

                case 4:
                    videoNavigator.CurrentValue = 50;
                    break;

               }
        
        }

        private void UpdateTracker(double timestamp)
        {
            if(m_tracker != null)
            {
                int roiX=0, roiY=0, roiW=0, roiH=0;
                if(m_tracker.Update(m_vm.lastManualImageByteArray, m_vm.width, m_vm.height, ref roiX, ref roiY, ref roiW, ref roiH))
                {
                    //ObservableCollection<FrameEdit> list1 = m_editsDB.GetRedactionListForTimestamp(timestamp);
                    //NonMaximumSuppression nms = new NonMaximumSuppression();
                    //List<DNNTools.BoundingBox> list2 = new List<DNNTools.BoundingBox>();
                    //foreach(FrameEdit fe in list1)
                    //{

                    //}
                    //List<DNNTools.BoundingBox> list3 = nms.Execute(list2, 0.40f);
                    //if(list3.Count > 1)
                    //{

                    //}
                    
                    m_editsDB.AddFrameEdit(timestamp, FRAME_EDIT_TYPE.TRACKING_REDACTION, new VideoTools.BoundingBox(roiX, roiY, roiX + roiW - 1, roiY + roiH - 1));
                    m_vm.RedrawRedactionBoxes_Manual();
                }
                else
                {
                    // tracker failed
                    TrackPB.IsChecked = false;
                }
            }
        }


        private void AutoRedact_StopPB_Click(object sender, RoutedEventArgs e)
        {
            m_pauseTokenSource.IsPaused = false;
            m_cancelTokenSource.Cancel();

            AutoRedact_PlayPB.IsEnabled = true;
            AutoRedact_PausePB.IsEnabled = false;
            AutoRedact_StopPB.IsEnabled = false;

            m_vm.state = AppState.READY;

            m_vm.autoImage.Clear();
            m_vm.autoOverlay.Clear();

            videoNavigator.CurrentValue = 0;
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

        private int _currentFrameIndex;
        public int currentFrameIndex
        {
            get { return _currentFrameIndex; }
            set
            {
                _currentFrameIndex = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("currentFrameIndex"));
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
                timestampStr = String.Format("{0:0.000} (" + currentFrameIndex.ToString() + ")", value);
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


        private byte[] _lastManualImageByteArray;
        public byte[] lastManualImageByteArray
        {
            get { return _lastManualImageByteArray; }
            set
            {
                _lastManualImageByteArray = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("lastManualImageByteArray"));
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

        private string _manualMessage;
        public string manualMessage
        {
            get { return _manualMessage; }
            set { _manualMessage = value; PropertyChanged(this, new PropertyChangedEventArgs("manualMessage")); }
        }


        public void RedrawRedactionBoxes_Manual()
        {
            if (manualOverlay != null)
            {
                manualOverlay.Clear();

                foreach (FrameEdit fe in redactions)
                {
                    if (fe == selectedRedaction)
                        manualOverlay.FillRectangle(fe.box.x1, fe.box.y1, fe.box.x2, fe.box.y2, selectedFillColor);
                    else
                        manualOverlay.FillRectangle(fe.box.x1, fe.box.y1, fe.box.x2, fe.box.y2, fillColor);
                }
            }
        }



        public void RedrawRedactionBoxes_Auto()
        {
            if (autoOverlay != null)
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
        }



        public void SetManualImage(int Width, int Height, int Depth, byte[] data)
        {   
            if(Width != width || Height != height || manualImage == null || manualOverlay == null)
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
                    width = Width;
                    height = Height;
                    depth = Depth;
                    PixelFormat pf = PixelFormats.Bgr24;
                    if (depth > 3) pf = PixelFormats.Bgra32;
                    autoImage = new WriteableBitmap(width, height, 96, 96, pf, null);
                    autoOverlay = BitmapFactory.New(width, height);
                }
            }
            else
            {
                width = Width;
                height = Height;
                depth = Depth;
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

            _manualMessage = "";
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
