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
using System.Windows.Data;
using System.Globalization;
using System.Collections.Concurrent;

namespace RedactEQ
{
    
    public enum BackgroundTaskRunning
    {
        NONE,
        DETECTOR,
        TRACKER,
        PLAYER
    }
   

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

        private CudaTools m_cudaTools;

        ITargetBlock<Tuple<ImagePackage, WriteableBitmap, WriteableBitmap, bool>> m_pipeline;

        VideoEditsDatabase m_editsDB;
        int m_maxFrameIndex;
        bool m_dragging;
        Point m_p1, m_p2;

        bool m_dnnLoaded;

        ITargetBlock<Tuple<int>> m_cachePipeline;

        bool m_tracking = false;
        Int32Rect m_startingTrackingRect;
        CVTracker m_tracker;

        int m_manualAutoStep;

        long m_currentMp4FileDurationMilliseconds = 0;

        BackgroundTaskRunning m_activeBackgroundTask;

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
            
            videoNavigator.RangeChanged += VideoNavigator_RangeChanged;
            
            m_activeBackgroundTask = BackgroundTaskRunning.NONE;

            m_cudaTools = new CudaTools();
            m_cudaTools.Init();
            m_dnnLoaded = false;

        }

        private void VideoNavigator_RangeChanged(object sender, RangeSliderEventArgs e)
        {
            if (e.Current != m_vm.timestamp)
            {

                double targetTimestamp = e.Current;
                int frameIndex;
                double timestamp;
                double percentPosition;

                if (m_videoCache != null)
                {
                    if (m_videoCache.GetClosestFrameIndex(targetTimestamp, out frameIndex, out timestamp, out percentPosition))
                    {
                        m_cachePipeline.Post(Tuple.Create<int>(frameIndex));

                        KillAnyBackgroundMP4ReaderTasks(); // this action will get any paused MP4Reader out of sync, to just kill it
                    }
                }
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (m_editsDB != null) m_editsDB.SaveDatabase();
            if (m_cancelTokenSource != null) m_cancelTokenSource.Cancel();
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


        private void M_videoCache_VideoCacheEvent(object sender, VideoCache_EventArgs e)
        {
            switch(e.status)
            {
                case VideoCache_Status_Type.REACHED_END_OF_FILE:
                case VideoCache_Status_Type.REACHED_BEGINNING_OF_FILE:
                    m_tracking = false;
                    m_vm.state = AppState.READY;
                    break;
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
                            m_vm.currentGopIndex = e.frame.gopIndex;
                            m_vm.currentGopTimestamp = e.frame.gopTimestamp;
                            
                            m_vm.SetImage(e.frame.imagePackage.width, e.frame.imagePackage.height, e.frame.imagePackage.numChannels, e.frame.imagePackage.data);
                            m_vm.lastImageByteArray = e.frame.imagePackage.data;
                            if (m_tracking)
                            {
                                UpdateTracker(e.frame.timestamp);

                                if (m_manualAutoStep > 0)
                                    Step_Forw();
                                else if (m_manualAutoStep < 0)
                                    Step_Prev();
                            }

                            if(m_vm.state == AppState.DETECTOR_RUNNING)
                            {
                                Detect_Current_Image_Slow();

                                if (m_manualAutoStep > 0)
                                    Step_Forw();
                                else if (m_manualAutoStep < 0)
                                    Step_Prev();
                            }

                            m_vm.timestamp = e.frame.timestamp;

                            m_vm.redactions = m_editsDB.GetRedactionListForTimestamp(e.frame.timestamp);
                            m_vm.RedrawRedactionBoxes();

                            //double durationOfEntireVideo = (double)m_videoCache.GetVideoDuration() / 1000.0;
                            //double percentDone = e.frame.timestamp / durationOfEntireVideo * 100.0f;
                            videoNavigator.CurrentValue = e.frame.timestamp;

                            //if (videoNavigator.CurrentValue < videoNavigator.LowerValue) videoNavigator.LowerValue = videoNavigator.CurrentValue;
                            //if (videoNavigator.CurrentValue > videoNavigator.UpperValue) videoNavigator.UpperValue = videoNavigator.CurrentValue;

                        }));
                    }
                    break;
            }

        }


        private string BuildEditedFilename(string filename)
        {
            string editedFilename = filename;

            editedFilename.Replace(".mp4", "_edited.mp4");

            return editedFilename;
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


        private void Step_Prev()
        {
            if(m_vm.currentFrameIndex > 0)
            {
                int index = m_vm.currentFrameIndex - 1;

                m_cachePipeline.Post(Tuple.Create<int>(index));

                KillAnyBackgroundMP4ReaderTasks();  // this action will get any paused MP4Reader out of sync, to just kill it
            }            
        }

        private void Step_Forw()
        {
            if(m_vm.currentFrameIndex < m_maxFrameIndex)
            {
                int index = m_vm.currentFrameIndex + 1;

                m_cachePipeline.Post(Tuple.Create<int>(index));

                KillAnyBackgroundMP4ReaderTasks();  // this action will get any paused MP4Reader out of sync, to just kill it
            }
        }

        private void KillAnyBackgroundMP4ReaderTasks()
        {
            if(m_pauseTokenSource != null)
            {
                m_pauseTokenSource.IsPaused = false;
            }

            if(m_cancelTokenSource != null)
            {
                m_cancelTokenSource.Cancel();
            }
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


        //private void Update_Image(double timestamp, int width, int height, int depth, byte[] data)
        //{
        //    m_vm.SetImage(width, height, depth, data);
        //    m_vm.timestamp = timestamp;
        //}

        private void ImageOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            m_dragging = true;
            m_p1 = ConvertToOverlayPosition(e.GetPosition(ImageOverlay), ImageDisplay, m_vm.imageOverlay);
            m_p2 = m_p1;
        }

        private void ImageOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            m_dragging = false;
            int x1, x2, y1, y2;
            GetDrawPoints(out x1, out y1, out x2, out y2);

            if (m_vm.state == AppState.WAITING_FOR_TRACKING_REGION_SELECTION)
            {
                m_startingTrackingRect = new Int32Rect(x1, y1, x2 - x1 + 1, y2 - y1 + 1);

                // Create a tracker
                m_tracker = new CVTracker();
                m_tracker.Init(TrackerType.KCF);
                if (m_vm.lastImageByteArray != null)
                {
                    if (m_tracker.StartTracking(m_vm.lastImageByteArray, m_vm.width, m_vm.height,
                        m_startingTrackingRect.X, m_startingTrackingRect.Y, m_startingTrackingRect.Width, m_startingTrackingRect.Height))
                    {
                        m_tracking = true;
                        m_vm.messageToUser = "Ready to Track";
                        m_vm.state = AppState.READY_TO_TRACK;

                        if (m_editsDB != null)
                        {                            
                            m_editsDB.AddFrameEdit(m_vm.timestamp, FRAME_EDIT_TYPE.TRACKING_REDACTION, new VideoTools.BoundingBox(x1, y1, x2, y2));
                        }

                        m_vm.RedrawRedactionBoxes();
                    }
                    else
                    {
                        MessageBox.Show("Failed to Initialize Tracker", "Tracker Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        m_tracking = false;
                        m_vm.messageToUser = "";
                        Track_Enable_RegionPick_PB.IsChecked = false;
                    }
                }


                
            }
            else if(m_vm.state == AppState.READY)
            {
                if (m_editsDB != null)
                {                    
                    m_editsDB.AddFrameEdit(m_vm.timestamp, FRAME_EDIT_TYPE.MANUAL_REDACTION, new VideoTools.BoundingBox(x1, y1, x2, y2));
                }

                m_vm.RedrawRedactionBoxes();
            }

        }

        private void ImageOverlay_MouseLeave(object sender, MouseEventArgs e)
        {
            if (m_dragging)
            {
                m_vm.imageOverlay.Clear();
                Int32Rect rect = new Int32Rect(0, 0, (int)m_vm.imageOverlay.Width, (int)m_vm.imageOverlay.Height);
            }

            m_dragging = false;
        }

        private void ImageOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (m_dragging)
            {
                int x1, x2, y1, y2;

                m_vm.RedrawRedactionBoxes();

                // draw new (the one we're dragging that is not yet in the collection)
                m_p2 = ConvertToOverlayPosition(e.GetPosition(ImageOverlay), ImageDisplay, m_vm.imageOverlay);
                GetDrawPoints(out x1, out y1, out x2, out y2);
                m_vm.imageOverlay.FillRectangle(x1, y1, x2, y2, m_vm.fillColor);                
            }
        }

        private void ImageOverlay_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Point pt = ConvertToOverlayPosition(e.GetPosition(ImageOverlay), ImageDisplay, m_vm.imageOverlay);

            int x = (int)pt.X;
            int y = (int)pt.Y;

            FrameEdit deleteThisOne = null;

            foreach(FrameEdit fe in m_vm.redactions)
            {
                if(x > fe.box.x1 && x < fe.box.x2 &&  y > fe.box.y1 && y < fe.box.y2)
                {
                    deleteThisOne = fe;
                    break;
                }                    
            }

            if (deleteThisOne != null)
            {
                m_vm.redactions.Remove(deleteThisOne);
                m_vm.RedrawRedactionBoxes();
            }
        }



        private void GOP_ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //m_currentGOPindex = m_vm.selectedGOP.index;
            //double gopTimestamp = m_vm.selectedGOP.timestamp;

            //Update(m_currentGOPindex, false);
        }


        public double GetTimestampFromSliderValue(double value)
        {
            return (double)value / 100.0 * (double)m_videoCache.GetVideoDuration() / 1000.0;
        }



        private void Player_Stop()
        {
            m_pauseTokenSource.IsPaused = false;
            m_cancelTokenSource.Cancel();

            m_vm.state = AppState.READY;
        }

        public void Player_Start(string filename, int decodeWidth, int decodeHeight, bool paceOutput)
        {
            VideoTools.Mp4Reader mp4Reader = new VideoTools.Mp4Reader();
            m_cancelTokenSource = new CancellationTokenSource();
            m_pauseTokenSource = new WPFTools.PauseTokenSource();
            m_pauseTokenSource.IsPaused = false;
            double startTimestamp = m_vm.timestamp;
            double endTimestamp = videoNavigator.Maximum;
            m_vm.state = AppState.PLAYER_RUNNING;
            m_activeBackgroundTask = BackgroundTaskRunning.PLAYER;
            mp4Reader.StartPlayback_1(filename, NewFrame_From_Mp4Reader, decodeWidth, decodeHeight, startTimestamp, endTimestamp, null, 0.70f, false,
                m_cancelTokenSource, m_pauseTokenSource, paceOutput, m_videoCache.m_gopIndexLookup);
        }


        public void NewFrame_From_Mp4Reader(VideoTools.ProgressStruct frame)
        {
            if (frame.timestamp == -0.001 || frame.timestamp == -1 || frame.finished)
            {
                Player_Stop();
                m_activeBackgroundTask = BackgroundTaskRunning.NONE;
                m_vm.state = AppState.READY;
                if(!frame.finished) Nav_Goto_Start_PB_Click(null, null);
            }
            else
            {
                int w = frame.width;
                int h = frame.height;
                double timestamp = frame.timestamp;
                int frameIndex = frame.frameIndex;
                int byteCount = frame.width * frame.height * 3;
            
                m_vm.lastImageByteArray = frame.data;


                // handle new frame coming in             
                m_vm.redactions = m_editsDB.GetRedactionListForTimestamp(frame.timestamp);

                if ((bool)Player_Mode_Redacted.IsChecked)
                {   
                    if(m_vm.redactions.Count > 0)
                    {
                        byte[] redactedImage = new byte[byteCount];
                        GetRedactedImage(frame.data,w,h, m_vm.redactions, 16, out redactedImage);
                        Buffer.BlockCopy(redactedImage, 0, frame.data, 0, byteCount);
                    }
                }


                m_vm.SetImage(w,h, 3, frame.data);

                //double durationOfEntireVideo = (double)m_videoCache.GetVideoDuration() / 1000.0;
                //double percentDone = timestamp / durationOfEntireVideo * 100.0f;
                videoNavigator.CurrentValue = frame.timestamp;
                
                m_vm.currentFrameIndex = frameIndex;
                m_vm.timestamp = timestamp;

                if ((bool)Player_Mode_Edited.IsChecked)
                {                    
                    m_vm.RedrawRedactionBoxes();
                }


                if (frame.key)
                {
                    m_vm.currentGopTimestamp = frame.timestamp;
                    m_vm.currentGopIndex = m_videoCache.GetGopIndexContainingFrameIndex(frame.frameIndex, m_videoCache.m_gopList);
                }


            }
        }



        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        private void Detect_CurrentImage_PB_Click(object sender, RoutedEventArgs e)
        {
            if (m_vm.state == AppState.READY)
            {
                if (!m_dnnLoaded) LoadDNN();

                if(m_dnnLoaded)
                    Detect_Current_Image();
            }
        }

        private void Detect_Run_PB_Click(object sender, RoutedEventArgs e)
        {

            if (m_vm.state == AppState.READY)
            {
                if (!m_dnnLoaded) LoadDNN();

                if (m_dnnLoaded)
                {
                    switch (m_activeBackgroundTask) // check to see what (if any) background tasks are already running
                    {
                        case BackgroundTaskRunning.NONE:
                        case BackgroundTaskRunning.PLAYER:
                        case BackgroundTaskRunning.TRACKER:
                            // make sure anything that might be running in background is killed, then create new pause and cancel tokens
                            KillAnyBackgroundMP4ReaderTasks();
                            if (m_vm.mp4Filename != null)
                                if (File.Exists(m_vm.mp4Filename))
                                {
                                    Detection_Start(m_vm.mp4Filename, 640, 480, false);
                                }
                            break;
                        case BackgroundTaskRunning.DETECTOR:
                            // unpause the current task
                            if (m_pauseTokenSource != null) m_pauseTokenSource.IsPaused = false;
                            m_vm.state = AppState.DETECTOR_RUNNING;
                            break;
                    }
                }

            }
        }


        public void Detection_Start(string filename, int decodeWidth, int decodeHeight, bool paceOutput)
        {
            VideoTools.Mp4Reader mp4Reader = new VideoTools.Mp4Reader();
            m_cancelTokenSource = new CancellationTokenSource();
            m_pauseTokenSource = new WPFTools.PauseTokenSource();
            m_pauseTokenSource.IsPaused = false;
            double startTimestamp = m_vm.timestamp;
            double endTimestamp = videoNavigator.Maximum;
            m_vm.state = AppState.DETECTOR_RUNNING;
            m_activeBackgroundTask = BackgroundTaskRunning.DETECTOR;
            mp4Reader.StartPlayback_1(filename, NewFrame_From_Mp4Reader_For_Detection, decodeWidth, decodeHeight, startTimestamp, endTimestamp, m_engine, 0.70f, false,
                m_cancelTokenSource, m_pauseTokenSource, paceOutput, m_videoCache.m_gopIndexLookup);
        }


        public void NewFrame_From_Mp4Reader_For_Detection(VideoTools.ProgressStruct frame)
        {
            if (frame.timestamp == -1 || frame.timestamp == -0.001 || frame.finished)
            {
                Player_Stop();
                m_activeBackgroundTask = BackgroundTaskRunning.NONE;
                m_vm.state = AppState.READY;
                if (!frame.finished) Nav_Goto_Start_PB_Click(null, null);
            }
            else
            {  
                // handle new frame coming in
                BitmapSource bs = BitmapSource.Create(frame.width, frame.height, 96, 96,
                                PixelFormats.Bgr24, null, frame.data, frame.width * 3);

                m_vm.imageDisplay = new WriteableBitmap(bs);

                //double durationOfEntireVideo = (double)m_videoCache.GetVideoDuration() / 1000.0;
                //double percentDone = frame.timestamp / durationOfEntireVideo * 100.0f;
                videoNavigator.CurrentValue = frame.timestamp;

                m_vm.currentFrameIndex = frame.frameIndex;
                m_vm.timestamp = frame.timestamp;

                //Detect_Current_Image();
                if(frame.boxList.Count > 0)
                {
                    int w = frame.width;
                    int h = frame.height;

                    // clean up duplicates
                    frame.boxList = m_nms.Execute(frame.boxList, 0.50f);

                    // make sure we don't have duplicates with what is already in edits database
                    List<DNNTools.BoundingBox> boxList_inEditsDB = m_editsDB.GetBoundingBoxesForTimestamp(m_vm.timestamp, w, h);
                    boxList_inEditsDB = m_nms.Execute(frame.boxList, boxList_inEditsDB, 0.50f);

                    // update edits database
                    m_editsDB.UpdateRedactions(m_vm.timestamp, boxList_inEditsDB, w, h);

                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        // update viewmodel on ui thread
                        m_vm.redactions = m_editsDB.GetEditsForFrame(m_vm.timestamp);
                        m_vm.RedrawRedactionBoxes();

                        if (frame.key)
                        {
                            m_vm.currentGopTimestamp = frame.timestamp;
                            m_vm.currentGopIndex = m_videoCache.GetGopIndexContainingFrameIndex(frame.frameIndex, m_videoCache.m_gopList);
                        }

                    }));
                }
            }
        }






        private async void Detect_Current_Image()
        {
            if (m_vm.lastImageByteArray != null)
            {
                await Task.Run(() =>
                {
                    byte[] data = m_vm.lastImageByteArray;
                    int w = m_vm.width;
                    int h = m_vm.height;
                    int d = m_vm.depth;

                    List<DNNTools.BoundingBox> boxList = m_engine.EvalImage(data, w, h, d, w, h, 0.70f);

                    // clean up duplicates
                    boxList = m_nms.Execute(boxList, 0.50f);

                    // make sure we don't have duplicates with what is already in edits database
                    List<DNNTools.BoundingBox> boxList_inEditsDB = m_editsDB.GetBoundingBoxesForTimestamp(m_vm.timestamp, w, h);
                    boxList_inEditsDB = m_nms.Execute(boxList, boxList_inEditsDB, 0.50f);

                    // update edits database
                    m_editsDB.UpdateRedactions(m_vm.timestamp, boxList_inEditsDB, w, h);

                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        // update viewmodel on ui thread
                        m_vm.redactions = m_editsDB.GetEditsForFrame(m_vm.timestamp);
                        m_vm.RedrawRedactionBoxes();
                    }));
                    
                });
            }
        }


        private void Detect_Current_Image_Slow()
        {
            if (m_vm.lastImageByteArray != null)
            {
              
                    byte[] data = m_vm.lastImageByteArray;
                    int w = m_vm.width;
                    int h = m_vm.height;
                    int d = m_vm.depth;

                    List<DNNTools.BoundingBox> boxList = m_engine.EvalImage(data, w, h, d, w, h, 0.70f);

                    // clean up duplicates
                    boxList = m_nms.Execute(boxList, 0.50f);

                    // make sure we don't have duplicates with what is already in edits database
                    List<DNNTools.BoundingBox> boxList_inEditsDB = m_editsDB.GetBoundingBoxesForTimestamp(m_vm.timestamp, w, h);
                    boxList_inEditsDB = m_nms.Execute(boxList, boxList_inEditsDB, 0.50f);

                    // update edits database
                    m_editsDB.UpdateRedactions(m_vm.timestamp, boxList_inEditsDB, w, h);

                    Application.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        // update viewmodel on ui thread
                        m_vm.redactions = m_editsDB.GetEditsForFrame(m_vm.timestamp);
                        m_vm.RedrawRedactionBoxes();
                    }));

            }
        }


        private void Track_ForwardOneImage_PB_Click(object sender, RoutedEventArgs e)
        {
            if (m_vm.state == AppState.READY_TO_TRACK)
            {
                m_manualAutoStep = 0;
                Step_Forw();
            }
        }

        private void Track_Run_PB_Click(object sender, RoutedEventArgs e)
        {
            if (m_vm.state == AppState.READY_TO_TRACK)
            {
                m_manualAutoStep = 1;
                Step_Forw();
                m_vm.state = AppState.TRACKER_RUNNING;
                m_activeBackgroundTask = BackgroundTaskRunning.TRACKER;
            }
        }

        private void Player_Play_PB_Click(object sender, RoutedEventArgs e)
        {
            if (m_vm.state == AppState.READY)
            {
                switch (m_activeBackgroundTask) // check to see what (if any) background tasks are already running
                {
                    case BackgroundTaskRunning.NONE:
                    case BackgroundTaskRunning.DETECTOR:
                    case BackgroundTaskRunning.TRACKER:
                        // make sure anything that might be running in background is killed, then create new pause and cancel tokens
                        KillAnyBackgroundMP4ReaderTasks();
                        if (m_vm.mp4Filename != null)
                            if (File.Exists(m_vm.mp4Filename))
                            {
                                Player_Start(m_vm.mp4Filename, 640, 480, true);
                            }
                        break;
              
                    case BackgroundTaskRunning.PLAYER:                        
                        // unpause the current task
                        if (m_pauseTokenSource != null) m_pauseTokenSource.IsPaused = false;
                        m_vm.state = AppState.PLAYER_RUNNING;
                        break;                      
  
                }

            }

        }


        private void Track_Enable_RegionPick_PB_Checked(object sender, RoutedEventArgs e)
        {
            m_vm.state = AppState.WAITING_FOR_TRACKING_REGION_SELECTION;
            m_vm.messageToUser = "Select Area to Track";
            m_tracking = false;
            m_startingTrackingRect = new Int32Rect(0, 0, 0, 0);            
        }

        private void Track_Enable_RegionPick_PB_Unchecked(object sender, RoutedEventArgs e)
        {
            m_vm.state = AppState.READY;
            m_tracking = false;
            m_vm.messageToUser = "";
        }

        private void Nav_Goto_Start_PB_Click(object sender, RoutedEventArgs e)
        {
            if(m_vm.state == AppState.READY)
            {
                m_cachePipeline.Post(Tuple.Create<int>(0));  // request 1st frame in video
            }
        }

        private void Nav_Goto_End_PB_Click(object sender, RoutedEventArgs e)
        {
            if (m_vm.state == AppState.READY)
            {
                int num = m_videoCache.GetNumFramesInVideo() - 1;                
                m_cachePipeline.Post(Tuple.Create<int>(num));  // request 1st frame in video             
            }
        }


        private void Nav_Pause_PB_Click(object sender, RoutedEventArgs e)
        {
            switch(m_vm.state)
            {
                case AppState.DETECTOR_RUNNING:
                    if(m_pauseTokenSource != null) m_pauseTokenSource.IsPaused = true;
                    m_vm.state = AppState.READY;
                    break;
                    
                case AppState.TRACKER_RUNNING:
                    m_activeBackgroundTask = BackgroundTaskRunning.NONE;
                    m_vm.state = AppState.READY;
                    break;

                case AppState.PLAYER_RUNNING:
                    if (m_pauseTokenSource != null) m_pauseTokenSource.IsPaused = true;
                    m_vm.state = AppState.READY;
                    break;
            }
        }

        private async void LoadDNN()
        {
            m_vm.isBusy = true;
            m_vm.messageToUser = "Initializing Neural Net ...";

            bool dnnLoadSucceeded = await Task.Run(() =>
            {
                bool success;
                success = InitDNN("D:/tensorflow/pretrained_models/Golden/1/frozen_inference_graph.pb",
                    "D:/tensorflow/pretrained_models/Golden/1/face_label_map.pbtxt");

                if (success)
                {
                    // send dummy image to DNN to force it to load                    
                    int w = 640;
                    int h = 480;
                    int d = 3;
                    byte[] data = new byte[w * h * d];
                    List<DNNTools.BoundingBox> boxList = m_engine.EvalImage(data, w, h, d, w, h, 0.70f);
                }
                return success;
            });

            if (dnnLoadSucceeded)
            {
                m_dnnLoaded = true;
            }
            else
            {
                MessageBox.Show("Failed to Load/Initialize DNN", "DNN Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            m_vm.isBusy = false;
        }

        private async void Menu_File_Open_Click(object sender, RoutedEventArgs e)
        {
            

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "MP4 (*.mp4)|*.mp4";
            openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (openFileDialog.ShowDialog() == true)
            {
                m_vm.isBusy = true;

                m_vm.mp4Filename = openFileDialog.FileName;
                
                bool result = await Task.Run(() =>
                {
                    bool success = true;

                    if (m_editsDB != null) m_editsDB.SaveDatabase();

                    m_editsDB = new VideoEditsDatabase(m_vm.mp4Filename);

                    if (success)
                    {
                        m_videoCache = new VideoCache("VideoCache", m_vm.mp4Filename, 2);

                        Application.Current.Dispatcher.Invoke(new Action(() =>
                        {
                            m_vm.messageToUser = "Initializing Video Cache ...";

                        }));

                        success = m_videoCache.Init(640, 480);

                        if(success)
                        {
                            m_videoCache.VideoCacheEvent += M_videoCache_VideoCacheEvent;

                            m_cachePipeline = m_videoCache.CreateCacheUpdatePipeline(2); // this parameter sets the number of Gops to cache on each side of the current Gop

                            m_maxFrameIndex = m_videoCache.GetMaxFrameIndex();

                            m_currentMp4FileDurationMilliseconds = m_videoCache.GetVideoDuration();

                            Application.Current.Dispatcher.Invoke(new Action(() =>
                            {
                                m_vm.messageToUser = "Initializing Video File GOP List ...";

                                videoNavigator.Maximum = (double)m_currentMp4FileDurationMilliseconds / 1000.0;
                                videoNavigator.CurrentValue = 0.0f;
                            }));

                            success = m_videoCache.GetGopList(m_vm.mp4Filename, null);

                            if (!success) m_errorMsg = "Failed to retrieve the GOP List from the video file";
                        }
                        else
                        {
                            m_errorMsg = "Failed to Initialize the Video Cache";
                        }
                    }
                    else
                    {
                        m_errorMsg = "Failed to Initialize Neural Net";
                    }

                    return success;
                });

                if(!result)
                {
                    MessageBox.Show("Initialization Error: " + m_errorMsg, "DNN Engine Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                }
           

                m_vm.isBusy = false;
                m_vm.state = AppState.READY;           

            }
        }

        private void Menu_File_Export_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Menu_File_LoadDNN_Click(object sender, RoutedEventArgs e)
        {
            LoadDNN();
        }


        private void Menu_File_Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Display_Grid_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (m_vm.state == AppState.READY)
            {
                if (e.Delta > 0)
                    Step_Forw();
                else
                    Step_Prev();
            }
        }

        private void Menu_Edit_ClearEditsDB_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("Are you sure you want to delete ALL edits for this video?", "Delete ALL Edits", MessageBoxButton.YesNo, MessageBoxImage.Question );
            if(result == MessageBoxResult.Yes)
            {
                m_editsDB.RemoveAllEdits();
                m_vm.RedrawRedactionBoxes();
            }            
        }

        private void Player_Mode_Original_Checked(object sender, RoutedEventArgs e)
        {
            if(m_vm != null)
            {
                if(m_vm.imageOverlay != null)
                    m_vm.imageOverlay.Clear();
            }
            
        }

        private void Player_Mode_Edited_Checked(object sender, RoutedEventArgs e)
        {
            if (m_vm != null)
            {
                if (m_vm.imageOverlay != null)
                {
                    m_vm.redactions = m_editsDB.GetRedactionListForTimestamp(m_vm.timestamp);
                    m_vm.RedrawRedactionBoxes();                    
                }
            }           
        }



        private void Player_Mode_Redacted_Checked(object sender, RoutedEventArgs e)
        {
            byte[] imageOut;
            
            if(GetRedactedImage(m_vm.lastImageByteArray, m_vm.width, m_vm.height, m_vm.redactions, 16, out imageOut ))
            {
                m_vm.SetImage(m_vm.width, m_vm.height, 3, imageOut);
            }
        }


        private bool GetRedactedImage(byte[] imageIn, int width, int height, ObservableCollection<FrameEdit> boxList, int blocksize, out byte[] imageOut)
        {
            bool success = true;
            
            m_cudaTools.RedactAreas_3(m_vm.lastImageByteArray, m_vm.width, m_vm.height, m_vm.redactions, blocksize, out imageOut);
            if (imageOut == null)
            {
                success = false;
            }

            return success;
        }

        private void NmsPB_Click(object sender, RoutedEventArgs e)
        {
            List<DNNTools.BoundingBox> boxList = new List<DNNTools.BoundingBox>();

            foreach(FrameEdit fe in m_vm.redactions)
            {
                float x1 = (float)fe.box.x1 / (float)m_vm.width;
                float y1 = (float)fe.box.y1 / (float)m_vm.height;
                float x2 = (float)fe.box.x2 / (float)m_vm.width;
                float y2 = (float)fe.box.y2 / (float)m_vm.height;
                boxList.Add(new DNNTools.BoundingBox(x1,y1,x2,y2, (int)fe.type, 0, 1.0f));
            }

            if (m_nms == null)
            {
                m_nms = new NonMaximumSuppression();
                m_nms.Init();
            }
            
            List<DNNTools.BoundingBox> cleanedBoxList = m_nms.Execute(boxList, 0.6f);

            m_vm.redactions.Clear();
            foreach(DNNTools.BoundingBox box in cleanedBoxList)
            {
                int x1 = (int)(box.x1 * (float)m_vm.width);
                int y1 = (int)(box.y1 * (float)m_vm.height);
                int x2 = (int)(box.x2 * (float)m_vm.width);
                int y2 = (int)(box.y2 * (float)m_vm.height);
                m_vm.redactions.Add(new FrameEdit((FRAME_EDIT_TYPE)box.classID, new VideoTools.BoundingBox(x1,y1,x2,y2)));
            }

            m_vm.RedrawRedactionBoxes();
        }

        private void DeleteRedactionPB_Click(object sender, RoutedEventArgs e)
        {
            if(m_vm.selectedRedaction != null)
            {
                FrameEdit fe = m_vm.selectedRedaction;
                m_vm.redactions.Remove(fe);
                m_vm.RedrawRedactionBoxes();
            }
        }

        private async void Menu_Edit_NMS_ALL_DB_Click(object sender, RoutedEventArgs e)
        {
            m_vm.isBusy = true;
            m_vm.messageToUser = "Cleaning up edits ...";

            bool nmsCleanUpSucceeded = await Task.Run(() =>
            {
                bool success = true;

                if (m_nms == null)
                {
                    m_nms = new NonMaximumSuppression();
                    m_nms.Init();
                }

                ObservableConcurrentDictionary<double, ObservableCollection<FrameEdit>> newEditsList = 
                                    new ObservableConcurrentDictionary<double, ObservableCollection<FrameEdit>>();

                foreach (KeyValuePair<double,ObservableCollection<FrameEdit>> item in m_editsDB.m_editsDictionary)
                {
                    List<DNNTools.BoundingBox> boxList = new List<DNNTools.BoundingBox>();

                    foreach (FrameEdit fe in item.Value)
                    {
                        float x1 = (float)fe.box.x1 / (float)m_vm.width;
                        float y1 = (float)fe.box.y1 / (float)m_vm.height;
                        float x2 = (float)fe.box.x2 / (float)m_vm.width;
                        float y2 = (float)fe.box.y2 / (float)m_vm.height;
                        boxList.Add(new DNNTools.BoundingBox(x1, y1, x2, y2, (int)fe.type, 0, 1.0f));
                    }

                    List<DNNTools.BoundingBox> cleanedBoxList = m_nms.Execute(boxList, 0.6f);

                    if(cleanedBoxList.Count > 0)
                    {
                        ObservableCollection<FrameEdit> list = new ObservableCollection<FrameEdit>();

                        foreach (DNNTools.BoundingBox box in cleanedBoxList)
                        {
                            int x1 = (int)(box.x1 * (float)m_vm.width);
                            int y1 = (int)(box.y1 * (float)m_vm.height);
                            int x2 = (int)(box.x2 * (float)m_vm.width);
                            int y2 = (int)(box.y2 * (float)m_vm.height);
                            list.Add(new FrameEdit((FRAME_EDIT_TYPE)box.classID, new VideoTools.BoundingBox(x1, y1, x2, y2)));
                        }

                        newEditsList.Add(item.Key, list);
                    }
                    
                 }


                m_editsDB.m_editsDictionary = newEditsList;
                
                return success;
            });

            if (nmsCleanUpSucceeded)
            {
                m_vm.redactions = m_editsDB.GetEditsForFrame(m_vm.timestamp);
                m_vm.RedrawRedactionBoxes();
            }
            else
            {
                MessageBox.Show("Failed to Clean Up Redundant Edits", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            m_vm.isBusy = false;
        }



        private void FrameEditsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            m_vm.RedrawRedactionBoxes();
        }




        private void UpdateTracker(double timestamp)
        {
            if (m_tracker != null)
            {
                int roiX = 0, roiY = 0, roiW = 0, roiH = 0;
                if (m_tracker.Update(m_vm.lastImageByteArray, m_vm.width, m_vm.height, ref roiX, ref roiY, ref roiW, ref roiH))
                {
                    m_editsDB.AddFrameEdit(timestamp, FRAME_EDIT_TYPE.TRACKING_REDACTION, new VideoTools.BoundingBox(roiX, roiY, roiX + roiW - 1, roiY + roiH - 1));
                    m_vm.RedrawRedactionBoxes();
                }
                else
                {
                    // tracker failed
                    Track_Enable_RegionPick_PB.IsChecked = false;
                    m_activeBackgroundTask = BackgroundTaskRunning.NONE;
                }
            }
        }





    }


    public enum AppState
    {
        MP4_NOT_SET,
        READY,
        DETECTOR_RUNNING,
        TRACKER_RUNNING,
        PLAYER_RUNNING,
        WAITING_FOR_TRACKING_REGION_SELECTION,
        READY_TO_TRACK
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
                SetState();
            }
        }

        private bool _isBusy;
        public bool isBusy
        {
            get { return _isBusy; }
            set { _isBusy = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("isBusy")); }
        }


        /// /////////////////
        ///  State Variables

        private bool _detector_detectPB_isEnabled;
        public bool detector_detectPB_isEnabled
        {
            get { return _detector_detectPB_isEnabled; }
            set { _detector_detectPB_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("detector_detectPB_isEnabled"));}
        }

        private bool _detector_runPB_isEnabled;
        public bool detector_runPB_isEnabled
        {
            get { return _detector_runPB_isEnabled; }
            set { _detector_runPB_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("detector_runPB_isEnabled")); }
        }

        private bool _track_enable_tracking_ToggleButton_isEnabled;
        public bool track_enable_tracking_ToggleButton_isEnabled
        {
            get { return _track_enable_tracking_ToggleButton_isEnabled; }
            set { _track_enable_tracking_ToggleButton_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("track_enable_tracking_ToggleButton_isEnabled")); }
        }

        private bool _track_stepPB_isEnabled;
        public bool track_stepPB_isEnabled
        {
            get { return _track_stepPB_isEnabled; }
            set { _track_stepPB_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("track_stepPB_isEnabled")); }
        }

        private bool _track_runPB_isEnabled;
        public bool track_runPB_isEnabled
        {
            get { return _track_runPB_isEnabled; }
            set { _track_runPB_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("track_runPB_isEnabled")); }
        }

        private bool _player_playPB_isEnabled;
        public bool player_playPB_isEnabled
        {
            get { return _player_playPB_isEnabled; }
            set { _player_playPB_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("player_playPB_isEnabled")); }
        }

        private bool _player_mode_GroupBox_isEnabled;
        public bool player_mode_GroupBox_isEnabled
        {
            get { return _player_mode_GroupBox_isEnabled; }
            set { _player_mode_GroupBox_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("player_mode_GroupBox_isEnabled")); }
        }

        private bool _frame_edits_ListBox_isEnabled;
        public bool frame_edits_ListBox_isEnabled
        {
            get { return _frame_edits_ListBox_isEnabled; }
            set { _frame_edits_ListBox_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("frame_edits_ListBox_isEnabled")); }
        }

        private bool _videoNavigator_isEnabled;
        public bool videoNavigator_isEnabled
        {
            get { return _videoNavigator_isEnabled; }
            set { _videoNavigator_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("videoNavigator_isEnabled")); }
        }

        private bool _nav_goto_startPB_isEnabled;
        public bool nav_goto_startPB_isEnabled
        {
            get { return _nav_goto_startPB_isEnabled; }
            set { _nav_goto_startPB_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("nav_goto_startPB_isEnabled")); }
        }

        private bool _nav_pausePB_isEnabled;
        public bool nav_pausePB_isEnabled
        {
            get { return _nav_pausePB_isEnabled; }
            set { _nav_pausePB_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("nav_pausePB_isEnabled")); }
        }

        private bool _nav_goto_endPB_isEnabled;
        public bool nav_goto_endPB_isEnabled
        {
            get { return _nav_goto_endPB_isEnabled; }
            set { _nav_goto_endPB_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("nav_goto_endPB_isEnabled")); }
        }

        private bool _nmsPB_isEnabled;
        public bool nmsPB_isEnabled
        {
            get { return _nmsPB_isEnabled; }
            set { _nmsPB_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("nmsPB_isEnabled")); }
        }

        private bool _delete_redactionPB_isEnabled;
        public bool delete_redactionPB_isEnabled
        {
            get { return _delete_redactionPB_isEnabled; }
            set { _delete_redactionPB_isEnabled = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("delete_redactionPB_isEnabled")); }
        }


        private string _messageToUser;
        public string messageToUser
        {
            get { return _messageToUser; }
            set { _messageToUser = value; PropertyChanged(this, new PropertyChangedEventArgs("messageToUser")); }
        }


        public void SetState()
        {
            switch(state)
            {
                case AppState.MP4_NOT_SET:
                    detector_detectPB_isEnabled = false;
                    detector_runPB_isEnabled = false;
                    track_enable_tracking_ToggleButton_isEnabled = false;
                    track_stepPB_isEnabled = false;
                    track_runPB_isEnabled = false;
                    player_playPB_isEnabled = false;
                    player_mode_GroupBox_isEnabled = false;
                    frame_edits_ListBox_isEnabled = false;
                    videoNavigator_isEnabled = false;
                    nav_goto_startPB_isEnabled = false;
                    nav_pausePB_isEnabled = false;
                    nav_goto_endPB_isEnabled = false;
                    nmsPB_isEnabled = false;
                    //messageToUser = "Select MP4 File";
                    break;
                case AppState.READY:
                    detector_detectPB_isEnabled = true;
                    detector_runPB_isEnabled = true;
                    track_enable_tracking_ToggleButton_isEnabled = true;
                    track_stepPB_isEnabled = false;
                    track_runPB_isEnabled = false;
                    player_playPB_isEnabled = true;
                    player_mode_GroupBox_isEnabled = true;
                    frame_edits_ListBox_isEnabled = true;
                    videoNavigator_isEnabled = true;
                    nav_goto_startPB_isEnabled = true;
                    nav_pausePB_isEnabled = true;
                    nav_goto_endPB_isEnabled = true;
                    nmsPB_isEnabled = true;
                    messageToUser = "Ready";
                    break;
                case AppState.DETECTOR_RUNNING:
                    detector_detectPB_isEnabled = false;
                    detector_runPB_isEnabled = false;
                    track_enable_tracking_ToggleButton_isEnabled = false;
                    track_stepPB_isEnabled = false;
                    track_runPB_isEnabled = false;
                    player_playPB_isEnabled = false;
                    player_mode_GroupBox_isEnabled = false;
                    frame_edits_ListBox_isEnabled = false;
                    videoNavigator_isEnabled = false;
                    nav_goto_startPB_isEnabled = false;
                    nav_pausePB_isEnabled = true;
                    nav_goto_endPB_isEnabled = false;
                    nmsPB_isEnabled = false;
                    messageToUser = "Detector Running";
                    break;
                case AppState.TRACKER_RUNNING:
                    detector_detectPB_isEnabled = false;
                    detector_runPB_isEnabled = false;
                    track_enable_tracking_ToggleButton_isEnabled = false;
                    track_stepPB_isEnabled = false;
                    track_runPB_isEnabled = false;
                    player_playPB_isEnabled = false;
                    player_mode_GroupBox_isEnabled = false;
                    frame_edits_ListBox_isEnabled = false;
                    videoNavigator_isEnabled = false;
                    nav_goto_startPB_isEnabled = false;
                    nav_pausePB_isEnabled = true;
                    nav_goto_endPB_isEnabled = false;
                    nmsPB_isEnabled = false;
                    messageToUser = "Tracker Running";
                    break;
                case AppState.PLAYER_RUNNING:
                    detector_detectPB_isEnabled = false;
                    detector_runPB_isEnabled = false;
                    track_enable_tracking_ToggleButton_isEnabled = false;
                    track_stepPB_isEnabled = false;
                    track_runPB_isEnabled = false;
                    player_playPB_isEnabled = false;
                    player_mode_GroupBox_isEnabled = false;
                    frame_edits_ListBox_isEnabled = false;
                    videoNavigator_isEnabled = false;
                    nav_goto_startPB_isEnabled = false;
                    nav_pausePB_isEnabled = true;
                    nav_goto_endPB_isEnabled = false;
                    nmsPB_isEnabled = false;
                    messageToUser = "Player Running";
                    break;
                case AppState.WAITING_FOR_TRACKING_REGION_SELECTION:
                    detector_detectPB_isEnabled = false;
                    detector_runPB_isEnabled = false;
                    track_enable_tracking_ToggleButton_isEnabled = true;
                    track_stepPB_isEnabled = false;
                    track_runPB_isEnabled = false;
                    player_playPB_isEnabled = false;
                    player_mode_GroupBox_isEnabled = false;
                    frame_edits_ListBox_isEnabled = false;
                    videoNavigator_isEnabled = false;
                    nav_goto_startPB_isEnabled = false;
                    nav_pausePB_isEnabled = false;
                    nav_goto_endPB_isEnabled = false;
                    nmsPB_isEnabled = false;
                    messageToUser = "Select Tracking Region";
                    break;
                case AppState.READY_TO_TRACK:
                    detector_detectPB_isEnabled = false;
                    detector_runPB_isEnabled = false;
                    track_enable_tracking_ToggleButton_isEnabled = true;
                    track_stepPB_isEnabled = true;
                    track_runPB_isEnabled = true;
                    player_playPB_isEnabled = false;
                    player_mode_GroupBox_isEnabled = false;
                    frame_edits_ListBox_isEnabled = false;
                    videoNavigator_isEnabled = false;
                    nav_goto_startPB_isEnabled = false;
                    nav_pausePB_isEnabled = false;
                    nav_goto_endPB_isEnabled = false;
                    nmsPB_isEnabled = false;
                    messageToUser = "Ready to Track";
                    break;
  
            }
        }




        ///  END State Variables
        /// /////////////////
         




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


        private int _currentGopIndex;
        public int currentGopIndex
        {
            get { return _currentGopIndex; }
            set
            {
                _currentGopIndex = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("currentGopIndex"));
            }
        }


        private double _currentGopTimestamp;
        public double currentGopTimestamp
        {
            get { return _currentGopTimestamp; }
            set
            {
                _currentGopTimestamp = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("currentGopTimestamp"));
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
                mp4Filename_DisplayName = Path.GetFileName(value);
            }
        }


        private string _mp4Filename_DisplayName;
        public string mp4Filename_DisplayName
        {
            get { return _mp4Filename_DisplayName; }
            set
            {
                _mp4Filename_DisplayName = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("mp4Filename_DisplayName"));
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



        private WriteableBitmap _imageDisplay;
        public WriteableBitmap imageDisplay
        {
            get { return _imageDisplay; }
            set
            {
                _imageDisplay = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("imageDisplay"));
            }
        }

        private WriteableBitmap _imageOverlay;
        public WriteableBitmap imageOverlay
        {
            get { return _imageOverlay; }
            set
            {
                _imageOverlay = value; if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs("imageOverlay"));
            }
        }


        private byte[] _lastImageByteArray;
        public byte[] lastImageByteArray
        {
            get { return _lastImageByteArray; }
            set
            {
                _lastImageByteArray = value; if (PropertyChanged != null)
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
            RedrawRedactionBoxes();
        }

        public void DeleteAnnotation(FrameEdit redaction)
        {
            redactions.Remove(redaction);            
            RedrawRedactionBoxes();
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




        public void RedrawRedactionBoxes()
        {
            if (imageOverlay != null)
            {
                imageOverlay.Clear();

                foreach (FrameEdit fe in redactions)
                {
                    if (fe == selectedRedaction)
                        imageOverlay.FillRectangle(fe.box.x1, fe.box.y1, fe.box.x2, fe.box.y2, selectedFillColor);
                    else
                        imageOverlay.FillRectangle(fe.box.x1, fe.box.y1, fe.box.x2, fe.box.y2, fillColor);
                }
            }
        }



        public void SetImage(int Width, int Height, int Depth, byte[] data)
        {
            if (imageDisplay != null)
            {
                if (Width != imageDisplay.PixelWidth || Height != imageDisplay.PixelHeight)
                {
                    width = Width;
                    height = Height;
                    depth = Depth;
                    PixelFormat pf = PixelFormats.Bgr24;
                    if (depth > 3) pf = PixelFormats.Bgra32;
                    imageDisplay = new WriteableBitmap(width, height, 96, 96, pf, null);
                    imageOverlay = BitmapFactory.New(width, height);
                }
            }
            else
            {
                width = Width;
                height = Height;
                depth = Depth;
                PixelFormat pf = PixelFormats.Bgr24;
                if (depth > 3) pf = PixelFormats.Bgra32;
                imageDisplay = new WriteableBitmap(width, height, 96, 96, pf, null);
                imageOverlay = BitmapFactory.New(width, height);
            }

            imageOverlay.Clear();
            Int32Rect rect = new Int32Rect(0, 0, imageDisplay.PixelWidth, imageDisplay.PixelHeight);
            imageDisplay.Lock();
            imageDisplay.WritePixels(rect, data, imageDisplay.PixelWidth * depth, 0);
            imageDisplay.Unlock();
        }


        //////////////////////////////////////////////////////////////////////////////////////////////////
        // player members


        public MainViewModel()
        {
            _messageToUser = "Open Video File";

            width = 0;
            height = 0;
            
            timestamp = 0.0;
            
            _gopList = new ObservableCollection<GOP_KEY_FRAME>();

            state = AppState.MP4_NOT_SET;

            _deleteRedactionCommand = new WPFTools.RelayCommand(DeleteSelectedRedaction);

            _fillColor = Color.FromArgb(0x55, 0xff, 0x00, 0x00);

            _selectedFillColor = Color.FromArgb(0x55, 0xf9, 0xff, 0x33);

            isBusy = false;
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



    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var v = (bool)value;
                return v ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (InvalidCastException)
            {
                return Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
   
    }

}
