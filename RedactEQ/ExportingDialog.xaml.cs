using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RedactEQ
{
    
    public partial class ExportingDialog : Window
    {
        string m_inputFilename;
        string m_outputFilename;
        CancellationTokenSource m_cancelTokenSource;
        WPFTools.PauseTokenSource m_pauseTokenSource;
        double m_startTime;
        double m_endTime;
        int m_decodeWidth, m_decodeHeight;
        VideoTools.VideoEditsDatabase m_editsDB;
        CudaTools m_cudaTools;
        int m_totalFrameCount;

        bool m_finished;

        public ExportingDialog(Window parent, string inputFilename, string outputFilename, double startTime, double endTime, int totalFrameCount,
            int decodeWidth, int decodeHeight,
            VideoTools.VideoEditsDatabase editsDB, CudaTools cudaTools)
        {
            this.Owner = parent;

            InitializeComponent();

            m_inputFilename = inputFilename;
            m_outputFilename = outputFilename;
            m_startTime = startTime;
            m_endTime = endTime;
            m_decodeWidth = decodeWidth;
            m_decodeHeight = decodeHeight;
            m_editsDB = editsDB;
            m_cudaTools = cudaTools;
            m_totalFrameCount = totalFrameCount;

            MyProgressBar.Value = 0;
            m_finished = false;

            Export_Start();
        }
        
        private void CancelPB_Click(object sender, RoutedEventArgs e)
        {
            m_cancelTokenSource.Cancel();
            Close();
        }


        public void Export_Start()
        {
            VideoTools.Mp4Reader mp4Reader = new VideoTools.Mp4Reader();
            m_cancelTokenSource = new CancellationTokenSource();
            m_pauseTokenSource = new WPFTools.PauseTokenSource();
            m_pauseTokenSource.IsPaused = false;
            mp4Reader.StartPlayback(m_inputFilename, NewFrame, m_decodeWidth, m_decodeHeight, m_startTime, m_endTime, null, 0.70f, false,
                m_cancelTokenSource, m_pauseTokenSource, false, null);
        }


        public void NewFrame(VideoTools.ProgressStruct frame)
        {
            if (frame.timestamp == -0.001 || frame.timestamp == -1 || frame.finished)
            {
                m_pauseTokenSource.IsPaused = false;
                m_cancelTokenSource.Cancel();
                
                if(frame.finished)
                {
                    m_finished = true;
                    CancelPB.Content = "Close";
                    FinishedImage.Visibility = Visibility.Visible;
                    // TODO: show dialog indicating finished exporting
                }

                //Close();
            }
            else
            {
                int w = frame.width;
                int h = frame.height;
                double timestamp = frame.timestamp;
                int frameIndex = frame.frameIndex;
                int byteCount = frame.width * frame.height * 3;

                // handle new frame coming in             
                ObservableCollection<VideoTools.FrameEdit> boxList = m_editsDB.GetRedactionListForTimestamp(frame.timestamp);

                byte[] redactedImage = new byte[byteCount];
                m_cudaTools.RedactAreas_3(frame.data, w, h, boxList, 16, out redactedImage);
                
                if(redactedImage != null)
                {
                    // TODO: encode image and write to file
                }


                Application.Current.Dispatcher.Invoke(new Action(() =>
                {
                    double percentComplete = (double)frameIndex/(double)m_totalFrameCount * 100.0;

                    MyProgressBar.Value = percentComplete; 

                    FrameNumberTextBlock.Text = "Time: " + String.Format("{0:0.000}", frame.timestamp + "   Frame: " + frameIndex.ToString());
                }));


            }
        }



    }
}
