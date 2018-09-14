using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;

namespace DNNTools
{
    public class NonMaximumSuppression : IDisposable
    {
        private IntPtr nms = IntPtr.Zero;
        const string DLL_NAME = "DnnTools.dll";

        // constructor
        public NonMaximumSuppression()
        {
        }



        #region Resource Disposable

        // Flag: Has Dispose already been called?
        bool disposed = false;

        // Public implementation of Dispose pattern callable by consumers.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern.
        [HandleProcessCorruptedStateExceptions]
        [SecurityCriticalAttribute]
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Free any other managed objects here.
                }

                // Free any unmanaged objects here.
                try
                {
                    Shutdown();
                }
                catch (Exception e)
                {
                    // Catch any unmanaged exceptions
                }
                disposed = true;
            }

        }

        // Destructor (.NET Finalize)
        ~NonMaximumSuppression()
        {
            Dispose(false);
        }

        #endregion





        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Init_NonMaxSuppression")]
        //  bool Init_NonMaxSuppression(NonMaximumSuppression** pp_nms)
        static extern bool NMS_Init(out IntPtr pNms);

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public bool Init()
        {
            bool initialized = false;
            nms = new IntPtr(0);
            try
            {
                NMS_Init(out nms);
                initialized = true;
            }
            catch (Exception ex)
            {
                initialized = false;
                string errMsg = ex.Message;
            }
            return initialized;
        }


        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Shutdown_NonMaxSuppression")]
        // void Shutdown_NonMaxSuppression(NonMaximumSuppression* p_nms)
        static extern void NMS_Shutdown(IntPtr pNms);

        public void Shutdown()
        {
            try
            {
                NMS_Shutdown(nms);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Marshaling Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "PerformNMS")]      
        // bool PerformNMS(NonMaximumSuppression* p_nms, BoundingBox const* pBboxes, int boxCount,
        //                  float threshold, char** pData, int* count)
        static extern bool NMS_Execute(IntPtr pNms, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStruct)] BoundingBox[] bboxes,
            int boxCount, float threshold, out IntPtr pData, out int count);


        public List<BoundingBox> Execute(List<BoundingBox> boxes, float threshold)
        {
            List<BoundingBox> outBoxes = new List<BoundingBox>();
                        
            IntPtr pData = IntPtr.Zero;
            int count;

            bool success = NMS_Execute(nms, boxes.ToArray(), boxes.Count, threshold, out pData, out count);

            if (success)
            {
                var sizeInBytes = Marshal.SizeOf(typeof(BoundingBox));

                for (int i = 0; i < count; i++)
                {
                    IntPtr ins = new IntPtr(pData.ToInt64() + i * sizeInBytes);
                    BoundingBox bb = Marshal.PtrToStructure<BoundingBox>(ins);
                    outBoxes.Add(bb);
                }

                Release(pData);
            }

            return outBoxes;
        }


        public List<BoundingBox> Execute(List<BoundingBox> boxes1, List<BoundingBox> boxes2, float threshold)
        {
            List<BoundingBox> outBoxes = new List<BoundingBox>();

            IntPtr pData = IntPtr.Zero;
            int count;

            // combine the lists
            List<BoundingBox> boxesAll = new List<BoundingBox>();
            foreach (BoundingBox box1 in boxes1) boxesAll.Add(box1);
            foreach (BoundingBox box2 in boxes2) boxesAll.Add(box2);

            bool success = NMS_Execute(nms, boxesAll.ToArray(), boxesAll.Count, threshold, out pData, out count);

            if (success)
            {
                var sizeInBytes = Marshal.SizeOf(typeof(BoundingBox));

                for (int i = 0; i < count; i++)
                {
                    IntPtr ins = new IntPtr(pData.ToInt64() + i * sizeInBytes);
                    BoundingBox bb = Marshal.PtrToStructure<BoundingBox>(ins);
                    outBoxes.Add(bb);
                }

                Release(pData);
            }

            return outBoxes;
        }



        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ReleaseNMS")]
        // void ReleaseNMS(NonMaximumSuppression* p_nms, ItemListHandle hItems)
        static extern void NMS_Release(IntPtr hVector);

        private void Release(IntPtr hVector)
        {
            NMS_Release(hVector);
        }



    }
}


