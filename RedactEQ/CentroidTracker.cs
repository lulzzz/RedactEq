using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DNNTools
{
    class CentroidTracker
    {
        // m_maxDisappeared =  The number of consecutive frames an object is allowed to be marked 
        // as “lost/disappeared” until we deregister the object.
        int m_maxDisappeared;

        // m_nextObjectID = A counter used to assign unique IDs to each object. 
        // In the case that an object leaves the frame and does not come back for m_maxDisappeared 
        // frames, a new (next) object ID would be assigned.
        int m_nextObjectID;

        // m_objects = A dictionary that utilizes the object ID as the key and the centroid 
        // (x, y)-coordinates as the value.
        Dictionary<int, BoundingBox> m_objects;

        // m_disappeared =  Maintains number of consecutive frames (value) a particular object ID (key) 
        // has been marked as “lost”.
        Dictionary<int, int> m_disappeared;

        public CentroidTracker(int maxDisappeared = 50)
        {
            m_maxDisappeared = maxDisappeared;
            m_nextObjectID = 0;
            m_objects = new Dictionary<int, BoundingBox>();
            m_disappeared = new Dictionary<int, int>();
        }

        public void Reset()
        {
            m_objects.Clear();
            m_disappeared.Clear();
            m_nextObjectID = 0;
        }


        public void SetMaxDisappeared(int maxDisappeared)
        {
            m_maxDisappeared = maxDisappeared;
        }

        public void Register(BoundingBox centroid)
        {
            // when registering an object we use the next available object
            // ID to store the centroid
            m_objects.Add(m_nextObjectID, centroid);
            m_disappeared.Add(m_nextObjectID, 0);
            m_nextObjectID++;
        }


        public void Deregister(int objectID)
        {
            m_objects.Remove(objectID);
            m_disappeared.Remove(objectID);
        }

        public List<BoundingBox> GetBoundingBoxList()
        {
            List<BoundingBox> boxList = new List<BoundingBox>();
            foreach(KeyValuePair<int,BoundingBox> item in m_objects)
            {
                BoundingBox b = item.Value;
                b.objectID = item.Key;                
                boxList.Add(b);
            }
            return boxList;
        }


        public List<BoundingBox> Update(List<BoundingBox> inputBoxes)
        {
            
            // check to see if the list of input bounding box rectangles is empty
            if (inputBoxes.Count == 0)
            {
                // loop over any existing tracked objects and mark them as disappeared (increment their disappeared count)
                foreach (int key in m_disappeared.Keys.ToList())
                {
                    m_disappeared[key] += 1;

                    // if we have reached a maximum number of consecutive frames where a given object has been marked as
                    // missing, deregister it
                    if (m_disappeared[key] > m_maxDisappeared)
                        Deregister(key);
                }

                // return early as there are no centroids or tracking info to update               
                return GetBoundingBoxList();
            }


            // if we are currently not tracking any objects take the input centroids and register each of them
            if (m_objects.Count == 0)
            {
                foreach (BoundingBox box in inputBoxes)
                {
                    Register(box);
                }
            }
            else
            {
                // otherwise, are are currently tracking objects so we need to try to match the input centroids 
                // to existing object centroids

                // calculate the distance matrix.  If N = # of centroids in m_objects (number of object currently being tracked), and 
                // M = # of centroids in the input centroids array (number of objects found in image frame), then the distance matrix will
                // be an NxM array of distances.  This is basically a matrix of distances from any of the N objects to any of 
                // the M objects.

                Dictionary<int, float[]> distance = Calc_Distance_Matrix(inputBoxes);

                // for each existing object centroid, find the input centroid that is closest to it, 
                // and capture it's index in the input centroid array

                List<Tuple<int, int, float>> RowsCols = new List<Tuple<int, int, float>>();
                foreach (KeyValuePair<int, float[]> item in distance)
                {
                    int id = item.Key;
                    float[] rowDistances = item.Value;
                    float minDist = float.MaxValue;
                    int minNdx = -1;
                    for (int i = 0; i < rowDistances.Length; i++)
                    {
                        if (rowDistances[i] < minDist)
                        {
                            minDist = rowDistances[i];
                            minNdx = i;
                        }
                    }
                    RowsCols.Add(Tuple.Create<int, int, float>(id, minNdx, minDist));
                }

                // sort 
                RowsCols.Sort((x, y) => x.Item3.CompareTo(y.Item3));

                List<int> usedRows = new List<int>();
                List<int> usedCols = new List<int>();

                // iterate over RowsCols
                foreach (Tuple<int, int, float> item in RowsCols)
                {
                    int id = item.Item1;
                    int ndx = item.Item2;
                    float dist = item.Item3;

                    bool rowUsed = false;
                    bool colUsed = false;
                    foreach (int i in usedRows) if (i == id) { rowUsed = true; break; }
                    foreach (int i in usedCols) if (i == ndx) { colUsed = true; break; }

                    // if we have already examined either the row or column value before, ignore it
                    if (!rowUsed && !colUsed)
                    {
                        // otherwise, grab the object ID for the current row, set its new centroid, and reset the disappeared counter
                        m_objects[id] = inputBoxes[ndx];
                        m_disappeared[id] = 0;

                        // indicate that we have examined each of the row and column indexes, respectively
                        usedRows.Add(id);
                        usedCols.Add(ndx);
                    }
                }


                // find rows and cols we have not yet used                
                var unusedRows = usedRows.Except(distance.Keys).ToList();
                int[] ndxs = Enumerable.Range(0, inputBoxes.Count - 1).ToArray();
                var unusedCols = usedCols.Except(ndxs).ToList();

                // in the event that the number of object centroids is equal or greater than the number of input centroids
                // we need to check and see if some of these objects have potentially disappeared
                if (m_objects.Count >= inputBoxes.Count)
                {
                    // loop over the unused row indexes
                    foreach (int rowID in unusedRows)
                    {
                        m_disappeared[rowID] += 1;

                        // check to see if the number of consecutive frames the object has been marked "disappeared"
                        // for warrants deregistering the object
                        if (m_disappeared[rowID] > m_maxDisappeared)
                            Deregister(rowID);
                    }
                }
            }

            return GetBoundingBoxList();
        }






        public float Calc_Centroid_Distance(BoundingBox b1, BoundingBox b2)
        {
            return (float)Math.Sqrt(((b2.cx - b1.cx) * (b2.cx - b1.cx) + (b2.cy - b1.cy) * (b2.cy - b1.cy)));            
        }



        public Dictionary<int, float[]> Calc_Distance_Matrix(List<BoundingBox> boxes)
        {
            Dictionary <int, float[]> distance = new Dictionary<int, float[]>();

            foreach(KeyValuePair<int,BoundingBox> obj in m_objects)
            {                
                float[] dd = new float[boxes.Count];
                distance.Add(obj.Key, dd);
                for(int i = 0; i<boxes.Count; i++)
                {
                    float d = Calc_Centroid_Distance(obj.Value, boxes[i]);
                    dd[i] = d;
                }
            }
            
            return distance;
        }

    }
}
