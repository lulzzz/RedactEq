using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TensorFlow;


namespace DNNTools
{
    class DNNengine
    {
        private TFGraph m_graph;
        private TFSession m_session;
        private string m_modelFile;
        private Dictionary<int, string> m_classes;
        private string m_lastErrorMsg;

        

        public DNNengine()
        {
            m_lastErrorMsg = "";
        }

        ~DNNengine()
        {
            if (m_session != null)
            {
                //m_session.CloseSession();
                //m_session.Dispose();
            }
            if (m_graph != null)
            {
                m_graph.Dispose();
            }
        }

        public bool Init(string filename, Dictionary<int, string> classes)
        {
            bool success = true;

            try
            {
                m_modelFile = filename;
                m_classes = classes;

                m_graph = new TFGraph();
                byte[] model = File.ReadAllBytes(m_modelFile);
                m_graph.Import(new TFBuffer(model));

                m_session = new TFSession(m_graph);
            }
            catch (Exception ex)
            {
                m_lastErrorMsg = ex.Message;
                success = false;
            }

            return success;
        }


        public List<BoundingBox> EvalImageFile(string filename, float minConfidence)
        {
            List<BoundingBox> boxList = new List<BoundingBox>();

            var tensor = ImageUtil.CreateTensorFromImageFile(filename, TFDataType.UInt8);
            var runner = m_session.GetRunner();

            runner
                .AddInput(m_graph["image_tensor"][0], tensor)
                .Fetch(
                m_graph["detection_boxes"][0],
                m_graph["detection_scores"][0],
                m_graph["detection_classes"][0],
                m_graph["num_detections"][0]);

            var output = runner.Run();

            var boxes = (float[,,])output[0].GetValue(jagged: false);
            var scores = (float[,])output[1].GetValue(jagged: false);
            var classes = (float[,])output[2].GetValue(jagged: false);
            var num = (float[])output[3].GetValue(jagged: false);

            int numberOfImages = 1;
            int ndx = numberOfImages - 1;

            for (int i = 0; i < num[ndx]; i++)
            {
                if (scores[ndx, i] >= minConfidence)
                {
                    int classID = (int)classes[ndx, i];

                    BoundingBox box = new BoundingBox(boxes[ndx, i, 1], boxes[ndx, i, 0], boxes[ndx, i, 3], boxes[ndx, i, 2], 
                        classID, 0, scores[ndx,i]);
                    boxList.Add(box);
                }
            }

            return boxList;
        }



        public List<BoundingBox> EvalImage(byte[] imageData, int width, int height, int numChannels,
                                   int resizeWidth, int resizeHeight, float minConfidence)
        {
            List<BoundingBox> boxList = new List<BoundingBox>();

            var tensor = ImageUtil.CreateTensorFromBuffer(imageData, width, height, numChannels, resizeWidth, resizeHeight, TFDataType.UInt8);

            var runner = m_session.GetRunner();

            runner
                .AddInput(m_graph["image_tensor"][0], tensor)
                .Fetch(
                m_graph["detection_boxes"][0],
                m_graph["detection_scores"][0],
                m_graph["detection_classes"][0],
                m_graph["num_detections"][0]);

            var output = runner.Run();

            var boxes = (float[,,])output[0].GetValue(jagged: false);
            var scores = (float[,])output[1].GetValue(jagged: false);
            var classes = (float[,])output[2].GetValue(jagged: false);
            var num = (float[])output[3].GetValue(jagged: false);

            int numberOfImages = 1;
            int ndx = numberOfImages - 1;

            for (int i = 0; i < num[ndx]; i++)
            {
                if (scores[ndx, i] >= minConfidence)
                {
                    int classID = (int)classes[ndx, i];

                    BoundingBox box = new BoundingBox(boxes[ndx, i, 1], boxes[ndx, i, 0], boxes[ndx, i, 3], boxes[ndx, i, 2], 
                        classID, 0, scores[ndx,i]);
                    boxList.Add(box);
                }
            }

            return boxList;
        }


        public string GetLastError()
        {
            return m_lastErrorMsg;
        }



        public List<BoundingBox> NonMaxSuppression(List<BoundingBox> inputBoxes)
        {
            List<BoundingBox> outputBoxes = new List<BoundingBox>();

            // array of picked indices
            List<int> pick = new List<int>();

            // array of each box coordinate
            List<float> x1List = inputBoxes.Select(x => x.x1).ToList();
            List<float> y1List = inputBoxes.Select(x => x.y1).ToList();
            List<float> x2List = inputBoxes.Select(x => x.x2).ToList();
            List<float> y2List = inputBoxes.Select(x => x.y2).ToList();

            // calculate area array
            List<float> area = new List<float>();
            foreach (BoundingBox box in inputBoxes)
                area.Add((box.x2 - box.x1) * (box.y2 - box.y1));

            List<float> idxs = new List<float>();
            foreach (float val in y2List) idxs.Add(val);
            idxs.Sort();

            // keep looping while some indexes still remain in the indexes list
            while (idxs.Count > 0)
            {

            }

            return outputBoxes;
        }
     

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////
        // 
        // Dataflow Pipeline



        public ITargetBlock<Tuple<byte[], int, int, int, WriteableBitmap, bool, bool>> CreateDNNPipeline(
            string modelFile, Dictionary<int, string> classes,
            int analysisWidth, int analysisHeight, TFDataType destinationDataType, float minConfidence, TextBlock tb1, TextBlock tb2,           
            TaskScheduler uiTask,
            CancellationToken cancelToken)
        {
            // input parameters:
            // analysisWidth = pixel width expected by NN input
            // analysisHeight = pixel height expected by NN input
            // destinationDataType = data type expected by NN input

            TFGraph l_graph;
            TFSession l_session;
            string l_modelFile = modelFile;
            Dictionary<int, string> l_classes = classes;

            int l_analysisWidth = analysisWidth;
            int l_analysisHeight = analysisHeight;
            TFDataType l_destinationDataType = destinationDataType;
            float l_minConfidence = minConfidence;
            CancellationToken l_cancelToken = cancelToken;
            TaskScheduler l_uiTask = uiTask;
            TextBlock l_numDetectionsTextBlock = tb1;
            TextBlock l_numTrackersTextBlock = tb2;

            CentroidTracker m_tracker = new CentroidTracker(10);

            MultiTracker m_multiTracker = new MultiTracker();
            m_multiTracker.SetMaxNumFramesWithoutMatch(4);

            NonMaximumSuppression m_nms = new NonMaximumSuppression();
            m_nms.Init();

            l_graph = new TFGraph();
            byte[] l_model = File.ReadAllBytes(l_modelFile);
            l_graph.Import(new TFBuffer(l_model));
            l_session = new TFSession(l_graph);


            
            //////////////////////////////////////////////////////////////////////
            // PRE-PROCESS
            //
            // Construct graph to preprocess raw image
            // - The model was trained after with images scaled to resizeWidth X resizeHeight pixels.
            // - The colors, represented as R, G, B in 1-byte each were converted to
            //   float using (value - Mean)/Scale.

            const float Mean = 0; // 117;
            const float Scale = 1;

            TFGraph l_preprocessGraph = new TFGraph();
            TFOutput l_preprocessInput;
            TFOutput l_preprocessOutput;

            l_preprocessInput = l_preprocessGraph.Placeholder(TFDataType.UInt8);

            //l_preprocessOutput = l_preprocessGraph.Cast(l_preprocessGraph.Div(
            //    x: l_preprocessGraph.Sub(
            //        x: l_preprocessGraph.ResizeBilinear(
            //            images: l_preprocessGraph.ExpandDims(
            //                input: l_preprocessGraph.Cast(l_preprocessInput, DstT: TFDataType.Float),
            //                dim: l_preprocessGraph.Const(0, "make_batch")),
            //            size: l_preprocessGraph.Const(new int[] { l_analysisWidth, l_analysisHeight }, "size")),
            //        y: l_preprocessGraph.Const(Mean, "mean")),
            //    y: l_preprocessGraph.Const(Scale, "scale")), l_destinationDataType);

            //l_preprocessOutput = l_preprocessGraph.Cast(
            //                                l_preprocessGraph.ResizeBilinear(
            //                                        images:l_preprocessGraph.ExpandDims(
            //                                                                    input: l_preprocessInput, 
            //                                                                    dim: l_preprocessGraph.Const(0, "make_batch")), 
            //                                        size: l_preprocessGraph.Const(new int[] { l_analysisWidth, l_analysisHeight }, "size")),
            //                                l_destinationDataType);

            l_preprocessOutput = l_preprocessGraph.ExpandDims(input: l_preprocessInput, dim: l_preprocessGraph.Const(0, "make_batch"));
            
            TFSession l_preprocessSession = new TFSession(l_preprocessGraph);

            ////////////////////////////////////////////////////////////////////////////////////////////////
            // POST-PROCESS
            //
            //TFGraph l_postprocessGraph = new TFGraph();
            //TFOutput l_postprocessInput;
            //TFOutput l_postprocessOutput;

            //l_postprocessInput = l_postprocessGraph.Placeholder(TFDataType.Float);

            //l_postprocessOutput = l_postprocessGraph.NonMaxSuppression(boxes: l_postprocessInput, scores: l_postprocessGraph.Const(0, "make_batch"));

            //TFSession l_postprocessSession = new TFSession(l_postprocessGraph);


            ////////////////////////////////////////////////////////////////////////////////////////////////
            // DATAFLOW BLOCKS

            var PreprocessImage = new TransformBlock<Tuple<byte[], int, int, int, WriteableBitmap, bool, bool>, 
                                                     Tuple<byte[],int, int, WriteableBitmap, TFTensor, bool, bool>>(inputData =>
                  {
                      // INPUTS:
                      //  item 1 - image data array
                      //  item 2 - image pixel width
                      //  item 3 - image pixel height
                      //  item 4 - image number of channels
                      //  item 5 - bitmap, if not null, will be used to display results
                      //  item 6 - bool flag indicating whether to turn on/off NonMaximumSuppression (getting rid of overlapping boxes)
                      //  item 7 - bool flag indicating whether to enable tracking

                      // OUTPUT:
                      //  tensor holding the preprocessed image

                      byte[] data = inputData.Item1;
                      int imageWidth = inputData.Item2;
                      int imageHeight = inputData.Item3;
                      int numChannels = inputData.Item4;
                      WriteableBitmap bitmap = inputData.Item5;
                      bool useNMS = inputData.Item6;
                      bool useTracker = inputData.Item7;

                      try
                      {
                          var rawInputTensor = TFTensor.FromBuffer(new TFShape(imageHeight, imageWidth, numChannels), data, 0,
                                                                        imageWidth * imageHeight * numChannels);


                          var preprocessed = l_preprocessSession.Run(
                                      inputs: new[] { l_preprocessInput },
                                      inputValues: new[] { rawInputTensor },
                                      outputs: new[] { l_preprocessOutput });

                         return Tuple.Create<byte[], int, int, WriteableBitmap, TFTensor, bool, bool>(data, imageWidth, imageHeight, 
                                                                                                      bitmap, preprocessed[0],useNMS, useTracker);
                      }
                      catch (Exception ex)
                      {
                          m_lastErrorMsg = ex.Message;
                          return null;
                      }
                  },
                  new ExecutionDataflowBlockOptions
                  {
                      // TaskScheduler = uiTask,
                      CancellationToken = cancelToken,
                      MaxDegreeOfParallelism = 1
                  });



           var EvaluateImage = new TransformBlock<Tuple<byte[],int,int,WriteableBitmap,TFTensor,bool,bool>, Tuple<byte[],int,int,WriteableBitmap,List<BoundingBox>,int,int>>(inputData =>
           {
               // INPUTS:
               //  item 1 - image data array
               //  item 2 - image pixel width
               //  item 3 - image pixel height
               //  item 4 - image number of channels
               //  item 5 - bitmap, if not null, will be used to display results
               //  item 6 - bool flag indicating whether to turn on/off NonMaximumSuppression (getting rid of overlapping boxes)
               //  item 7 - bool flag indicating whether to enable tracking
               // OUTPUTS:
               //  list of bounding boxes of detected objects
               //  number of detections from the DNN
               //  number of active trackers

               byte[] data = inputData.Item1;
               int imageWidth = inputData.Item2;
               int imageHeight = inputData.Item3;
               WriteableBitmap bitmap = inputData.Item4;
               TFTensor tensor = inputData.Item5;
               bool useNMS = inputData.Item6;
               bool useTracker = inputData.Item7;

               int numDetections = 0;
               int numTrackers = 0;

               try
               {
                   var runner = l_session.GetRunner();
                   List<BoundingBox> boxList = new List<BoundingBox>();

                   runner
                        .AddInput(l_graph["image_tensor"][0], tensor)
                        .Fetch(
                        l_graph["detection_boxes"][0],
                        l_graph["detection_scores"][0],
                        l_graph["detection_classes"][0],
                        l_graph["num_detections"][0]);

                   var output = runner.Run();

                   var boxes = (float[,,])output[0].GetValue(jagged: false);
                   var scores = (float[,])output[1].GetValue(jagged: false);
                   var _classes = (float[,])output[2].GetValue(jagged: false);
                   var num = (float[])output[3].GetValue(jagged: false);

                   int numberOfImages = 1;
                   int ndx = numberOfImages - 1;

                    for (int i = 0; i < num[ndx]; i++)
                    {
                        if (scores[ndx, i] >= l_minConfidence)
                        {
                           int classID = (int)_classes[ndx, i];

                           BoundingBox box = new BoundingBox(boxes[ndx, i, 1], boxes[ndx, i, 0], boxes[ndx, i, 3], boxes[ndx, i, 2], 
                                                            classID, 0, scores[ndx,i]);
                           boxList.Add(box);                           
                        }
                    }

                   if (useNMS)
                   {
                       boxList = m_nms.Execute(boxList, 0.50f);
                   }

                   numDetections = boxList.Count;

                   if (useTracker)
                   {
                       List<BoundingBox> trackedBoxes = m_multiTracker.Update(data, imageWidth, imageHeight, boxList);

                       numTrackers = trackedBoxes.Count;

                       boxList.AddRange(trackedBoxes);
      
                       boxList = m_nms.Execute(boxList, 0.50f);
                   }
                   else
                   {
                       m_multiTracker.ClearTrackers();
                   }

                   return Tuple.Create<byte[],int,int,WriteableBitmap,List<BoundingBox>,int,int>(data,imageWidth,imageHeight,bitmap,boxList,numDetections,numTrackers);
               }
               catch (Exception ex)
               {
                   m_lastErrorMsg = ex.Message;
                   return null;
               }
           },
                new ExecutionDataflowBlockOptions
                {
                    // TaskScheduler = uiTask,
                    CancellationToken = cancelToken,
                    MaxDegreeOfParallelism = 1
                });




            var PlotResults = new ActionBlock<Tuple<byte[],int,int,WriteableBitmap,List<BoundingBox>,int,int>>(inputData =>
            {
                byte[] data = inputData.Item1;
                int imageWidth = inputData.Item2;
                int imageHeight = inputData.Item3;
                WriteableBitmap bitmap = inputData.Item4;
                List<BoundingBox> boxes = inputData.Item5;
                int numDetections = inputData.Item6;
                int numTrackers = inputData.Item7;

                try
                {
                    //convert data to 4 bytes per pixel
                    byte[] data1 = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    for (int r = 0; r < bitmap.PixelHeight; r++)
                        for (int c = 0; c < bitmap.PixelWidth; c++)
                        {
                            int ndx = (r * bitmap.PixelWidth * 3) + (c * 3);
                            int ndx1 = (r * bitmap.PixelWidth * 4) + (c * 4);

                            data1[ndx1 + 0] = data[ndx + 0];
                            data1[ndx1 + 1] = data[ndx + 1];
                            data1[ndx1 + 2] = data[ndx + 2];
                            data1[ndx1 + 3] = 255;
                        }

                    Int32Rect rect = new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
                    bitmap.Lock();
                    bitmap.WritePixels(rect, data1, bitmap.PixelWidth * 4, 0);
                    bitmap.Unlock();

                    foreach (BoundingBox box in boxes)
                    {
                        int x1 = (int)(box.x1 * bitmap.PixelWidth);
                        int y1 = (int)(box.y1 * bitmap.PixelHeight);
                        int x2 = (int)(box.x2 * bitmap.PixelWidth);
                        int y2 = (int)(box.y2 * bitmap.PixelHeight);
                        bitmap.DrawRectangle(x1, y1, x2, y2, Colors.Red);
                        bitmap.DrawRectangle(x1 + 1, y1 + 1, x2 - 1, y2 - 1, Colors.Red);
                        //bitmap.DrawRectangle(x1 + 2, y1 + 2, x2 - 2, y2 - 2, Colors.Red);
                        //bitmap.FillRectangle(x1, y1, x2, y2, Colors.Red);
                    }

                    if(l_numDetectionsTextBlock != null)  l_numDetectionsTextBlock.Text = numDetections.ToString();
                    if(l_numTrackersTextBlock != null) l_numTrackersTextBlock.Text = numTrackers.ToString();
                    
                }
                catch (Exception ex)
                {
                    m_lastErrorMsg = ex.Message;
                    return;
                }

            },
            new ExecutionDataflowBlockOptions
            {
                TaskScheduler = l_uiTask,
                CancellationToken = cancelToken,
                MaxDegreeOfParallelism = 1
            }
            );


            PreprocessImage.LinkTo(EvaluateImage);
            EvaluateImage.LinkTo(PlotResults);
            return PreprocessImage;


        }



    }




    [StructLayout (LayoutKind.Sequential,Pack = 1)]
    public struct BoundingBox
    {
        public float cx; // centroid x coordinate
        public float cy; // centroid y coordinate
        public float x1;
        public float y1;
        public float x2;
        public float y2;
        public float confidence;
        public int classID; // this is the id of class in the class/label collection for the neural net
        public int objectID; // this is a unique id of object detected.  This is used by the Tracker (if being used)
        public BoundingBox(float X1, float Y1, float X2, float Y2, int ClassID, int ObjectID, float Confidence)
        {
            cx = (X1+X2)/2.0f;
            cy = (Y1+Y2)/2.0f;
            x1 = X1;
            y1 = Y1;
            x2 = X2;
            y2 = Y2;
            classID = ClassID;
            objectID = ObjectID;
            confidence = Confidence;
        }      
    }

    
}
