using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;



namespace DNNTools
{
    public class TrackedObject
    {
        public CVTracker tracker;
        public BoundingBox box;
        public int numFramesWithoutMatch;
        public TrackedObject(CVTracker Tracker, BoundingBox Box)
        {
            tracker = Tracker;
            box = Box;
            numFramesWithoutMatch = 0;
        }
    }

    public class MultiTracker
    {

        Dictionary<int, TrackedObject> m_trackedObjects;
        int m_objectCount;
        int m_maxNumFramesWithoutMatch;

        public MultiTracker()
        {
            m_trackedObjects = new Dictionary<int, TrackedObject>();
            m_objectCount = 0;
            m_maxNumFramesWithoutMatch = 2;

            //m_trackedObjects.Add(10, new TrackedObject(null, new BoundingBox(1, 1, 3, 3, 1, 10, 0.9f)));    // Centroid: 2,2
            //m_trackedObjects.Add(11, new TrackedObject(null, new BoundingBox(8, 5, 10, 7, 1, 11, 0.9f)));   // Centroid: 9,6
            //m_trackedObjects.Add(12, new TrackedObject(null, new BoundingBox(1, 7, 3, 9, 1, 12, 0.9f)));    // Centroid: 2,8
            //m_trackedObjects.Add(13, new TrackedObject(null, new BoundingBox(5, 8, 7, 10, 1, 13, 0.9f)));   // Centroid: 6,9

            //List<BoundingBox> candidateBoxes = new List<BoundingBox>();
            //candidateBoxes.Add(new BoundingBox(2, 2, 4, 4, 1, 0, 0.9f));    // Centroid: 3,3
            //candidateBoxes.Add(new BoundingBox(7, 2, 9, 4, 1, 1, 0.9f));    // Centroid: 8,3
            //candidateBoxes.Add(new BoundingBox(1, 4, 3, 6, 1, 2, 0.9f));    // Centroid: 2,5
            //candidateBoxes.Add(new BoundingBox(2, 6, 4, 8, 1, 3, 0.9f));    // Centroid: 3,7
            //candidateBoxes.Add(new BoundingBox(7, 8, 9, 10, 1, 4, 0.9f));    // Centroid: 8,9

            //Dictionary<int, int> matches;
            //List<int> noMatches;
            //List<BoundingBox> newBoxes;
            //FindNewBoxes(candidateBoxes, 2.1f, out matches, out noMatches, out newBoxes);

        }

        ~MultiTracker()
        {
            //dispose of all trackers
            //foreach (KeyValuePair<int, TrackedObject> obj in m_trackedObjects)
            //{
            //    TrackedObject to = obj.Value;

            //    to.tracker.Shutdown();
            //}
        }


        public void SetMaxNumFramesWithoutMatch(int num)
        {
            m_maxNumFramesWithoutMatch = num;
        }


        public void ClearTrackers()
        {
            m_trackedObjects.Clear();
        }



        //public List<BoundingBox> Update(byte[] imageData, int width, int height, List<BoundingBox> boxes)
        //{

        //    if(boxes.Count > 0 && m_trackedObjects.Count == 0)
        //    {
        //        BoundingBox box = boxes[0];
        //        CVTracker tracker = new CVTracker();
        //        bool success = tracker.Init("CSRT");
        //        if (success)
        //        {
        //            int x1 = (int)(box.x1 * (float)width);
        //            int y1 = (int)(box.y1 * (float)height);
        //            int x2 = (int)(box.x2 * (float)width);
        //            int y2 = (int)(box.y2 * (float)height);
        //            int w = x2 - x1 + 1;
        //            int h = y2 - y1 + 1;
        //            tracker.StartTracking(imageData, width, height, x1, y1, w, h);
        //            TrackedObject obj = new TrackedObject(tracker, box);
        //            m_objectCount++;
        //            m_trackedObjects.Add(m_objectCount, obj);
        //        }
        //    }

        //    List<int> removeList = new List<int>();
   
        //    // pass new frame to all existing trackers
        //    foreach (KeyValuePair<int, TrackedObject> obj in m_trackedObjects)
        //    {
        //        int id = obj.Key;
        //        TrackedObject to = obj.Value;
        //        BoundingBox box = to.box;
        //        int x1 = (int)(box.x1 * (float)width);
        //        int y1 = (int)(box.y1 * (float)height);
        //        int x2 = (int)(box.x2 * (float)width);
        //        int y2 = (int)(box.y2 * (float)height);
        //        int w = x2 - x1 + 1;
        //        int h = y2 - y1 + 1;

        //        bool success = to.tracker.Update(imageData, width, height, ref x1, ref y1, ref w, ref h);

        //        if (success)
        //        {
        //            box.x1 = (float)x1 / (float)width;
        //            box.y1 = (float)y1 / (float)height;
        //            box.x2 = (float)(x1+w-1) / (float)width;
        //            box.y2 = (float)(y1+h-1) / (float)height;

        //            to.box = box;
        //        }
        //        else
        //        {
        //            removeList.Add(id);
        //        }
        //    }

        //    // remove failed tracks
        //    foreach (int id in removeList) m_trackedObjects.Remove(id);

        //    // return list of BoundingBoxes
        //    List<BoundingBox> trackedBoxes = new List<BoundingBox>();
        //    foreach (KeyValuePair<int, TrackedObject> obj in m_trackedObjects)
        //    {
        //        trackedBoxes.Add(obj.Value.box);
        //    }

        //    return trackedBoxes;

        //}




        public List<BoundingBox> Update(byte[] imageData, int width, int height, List<BoundingBox> boxes)
        {
            // Find new boxes
            Dictionary<int, int> matches;
            List<int> noMatches;
            List<BoundingBox> newBoxes;
            FindNewBoxes(boxes, 0.3f, out matches, out noMatches, out newBoxes);

            // start a new tracker for each new box
            foreach (BoundingBox box in newBoxes)
            {
                //CorrelationTracker tracker = new CorrelationTracker();
                CVTracker tracker = new CVTracker();
                bool success = tracker.Init(TrackerType.KCF);
                if (success)
                {
                    int x1 = (int)(box.x1 * (float)width);
                    int y1 = (int)(box.y1 * (float)height);
                    int x2 = (int)(box.x2 * (float)width);
                    int y2 = (int)(box.y2 * (float)height);
                    int w = x2 - x1 + 1;
                    int h = y2 - y1 + 1;
                    tracker.StartTracking(imageData, width, height, x1, y1, w, h);
                    TrackedObject obj = new TrackedObject(tracker, box);
                    m_objectCount++;
                    m_trackedObjects.Add(m_objectCount, obj);
                }
            }

            // handle no matches AND box too small
            List<int> toDelete = new List<int>();
            foreach (int id in noMatches)
            {
                TrackedObject obj = m_trackedObjects[id];
                obj.numFramesWithoutMatch++;
                m_trackedObjects[id] = obj;

                float bw = obj.box.x2 - obj.box.x1;
                float bh = obj.box.y2 - obj.box.y1;

                if (obj.numFramesWithoutMatch > m_maxNumFramesWithoutMatch) toDelete.Add(id);
            }

            // remove trackers that haven't had a match within m_maxNumFramesWithoutMatch
            foreach (int id in toDelete)
            {
                m_trackedObjects.Remove(id);
            }


            // clear no match count for those with a match
            foreach (KeyValuePair<int, int> obj in matches)
            {
                int id = obj.Key;
                TrackedObject trackedObj = m_trackedObjects[id];
                trackedObj.numFramesWithoutMatch = 0;
                m_trackedObjects[id] = trackedObj;
            }


            // pass new frame to all existing trackers
            List<int> removeList = new List<int>();
            foreach (KeyValuePair<int, TrackedObject> obj in m_trackedObjects)
            {
                int id = obj.Key;
                TrackedObject to = obj.Value;
                BoundingBox box = to.box;
                int x1 = (int)(box.x1 * (float)width);
                int y1 = (int)(box.y1 * (float)height);
                int x2 = (int)(box.x2 * (float)width);
                int y2 = (int)(box.y2 * (float)height);
                int w = x2 - x1 + 1;
                int h = y2 - y1 + 1;

                bool success = to.tracker.Update(imageData, width, height, ref x1, ref y1, ref w, ref h);

                if (success)
                {
                    box.x1 = (float)x1 / (float)width;
                    box.y1 = (float)y1 / (float)height;
                    box.x2 = (float)(x1 + w - 1) / (float)width;
                    box.y2 = (float)(y1 + h - 1) / (float)height;

                    to.box = box;
                }
                else
                {
                    removeList.Add(id);
                }
            }

            // remove failed tracks
            foreach (int id in removeList)
            {
                m_trackedObjects.Remove(id);
            }

            // return list of BoundingBoxes
            List<BoundingBox> trackedBoxes = new List<BoundingBox>();
            foreach (KeyValuePair<int, TrackedObject> obj in m_trackedObjects)
            {
                trackedBoxes.Add(obj.Value.box);
            }

            return trackedBoxes;

        }



        public void FindNewBoxes(List<BoundingBox> inputBoxes, float maxMatchingDistance, 
            out Dictionary<int,int> matches, 
            out List<int> noMatches, 
            out List<BoundingBox> newBoxes)
        {
            newBoxes = new List<BoundingBox>();

            Dictionary<int, float[]> distances = Calc_Distance_Matrix(inputBoxes);

            Dictionary<int, int[]> sortedIndexes = Calc_Sorted_Index_Matrix(distances);

            // mark all tracked objects as having "no matches".  If a match is found for an object, it is removed from this list later
            noMatches = new List<int>();
            foreach (int id in m_trackedObjects.Keys) noMatches.Add(id);

            // find matches
            matches = new Dictionary<int, int>();            
            foreach(KeyValuePair<int,int[]> pair in sortedIndexes)
            {
                int id = pair.Key;
                int[] ndxs = pair.Value;                
                foreach(int i in ndxs)
                {
                    if(distances[id][i] <= maxMatchingDistance && !matches.ContainsKey(id))
                    {
                        matches.Add(id, i);                        
                        noMatches.Remove(id);
                        break;
                    }
                }                
            }

            // find new boxes
            for(int i = 0; i<inputBoxes.Count; i++)
            {
                if (!matches.ContainsValue(i))
                    newBoxes.Add(inputBoxes[i]);
            }

        }


        public float Calc_Centroid_Distance(BoundingBox b1, BoundingBox b2)
        {
            return (float)Math.Sqrt(((b2.cx - b1.cx) * (b2.cx - b1.cx) + (b2.cy - b1.cy) * (b2.cy - b1.cy)));
        }



        public Dictionary<int,float[]> Calc_Distance_Matrix(List<BoundingBox> boxes)
        {
            // this function creates a dictionary, where each entry contains an array of distance values.
            // The key for each entry is the ID for key for the Dictionary of currently tracked objects.
            // The value for each entry is an array of distances from the this tracked object to each
            // of the bounding boxes that are passed into the function.

            // N = number of currently tracked objects, and M = number of new boxes
            int N = m_trackedObjects.Count;
            int M = boxes.Count;

            Dictionary<int, float[]> distance = new Dictionary<int, float[]>();
            
            foreach (KeyValuePair<int, TrackedObject> obj in m_trackedObjects)
            {
                int ndx = obj.Key;         
                TrackedObject trackedObj = obj.Value;
                BoundingBox trackedBox = trackedObj.box;
                float[] fArray = new float[M];
                for (int m = 0; m < M; m++)
                {                    
                    fArray[m] = Calc_Centroid_Distance(trackedBox, boxes[m]);                    
                }

                distance.Add(ndx, fArray);
            }

            return distance;
        }


        public Dictionary<int, int[]> Calc_Sorted_Index_Matrix(Dictionary<int, float[]> distanceMatrix)
        {
            Dictionary<int, int[]> ndxs = new Dictionary<int, int[]>();

            foreach (KeyValuePair<int, float[]> pair in distanceMatrix)
            {
                int id = pair.Key;

                List<float> nums = pair.Value.ToList<float>();                               
                List<float> output;
                List<int> perm;
                Sort<float>(nums, out output, out perm, new FloatComparer());
                ndxs.Add(id, perm.ToArray());
            }

            return ndxs;
        }



        void Sort<T>(List<T> input, out List<T> output, out List<int> permutation, IComparer<T> comparer)
        {
            if (input == null) { throw new ArgumentNullException("input"); }
            if (input.Count == 0)
            {
                // give back empty lists
                output = new List<T>();
                permutation = new List<int>();
                return;
            }
            if (comparer == null) { throw new ArgumentNullException("comparer"); }
            int[] items = Enumerable.Range(0, input.Count).ToArray();
            T[] keys = input.ToArray();
            Array.Sort(keys, items, comparer);
            output = keys.ToList();
            permutation = items.ToList();
        }


        public class FloatComparer : Comparer<float>
        {
            public override int Compare(float f1, float f2)
            {
                if (f1 > f2)
                    return 1;
                if (f1 < f2)
                    return -1;
                else
                    return 0;
            }
        }


    }
}
