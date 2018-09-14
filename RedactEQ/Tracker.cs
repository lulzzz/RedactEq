using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace DNNTools
{
    public enum TrackerType
    {
        BOOSTING,
        CSRT,
        KCF,
        MOSSE,
        TLD
    }

    public class CVTracker : IDisposable
    {

        const string DLL_NAME = "Track.dll";

        private IntPtr cvTracker = IntPtr.Zero;
        private string m_lastErrorMsg = "none";

        public CVTracker()
        {

        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // IDisposable 

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
        ~CVTracker()
        {
            Dispose(false);
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        public string GetLastError()
        {
            return m_lastErrorMsg;
        }


        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "CVInitTracker")]
        // bool CVInitTracker(CVObjectTracker** pp_object_tracker, string trackerType)
        static extern bool CVTracker_Init(out IntPtr tracker, string trackerType);

        public bool Init(TrackerType trackerType)
        {
            string type = "KCF";  // default

            switch(trackerType)
            {
                case TrackerType.BOOSTING:
                    type = "BOOSTING";
                    break;
                case TrackerType.CSRT:
                    type = "CSRT";
                    break;
                case TrackerType.KCF:
                    type = "KCF";
                    break;
                case TrackerType.MOSSE:
                    type = "MOSSE";
                    break;
                case TrackerType.TLD:
                    type = "TLD";
                    break;
                default:
                    type = "KCF";
                    break;
            }

            return CVTracker_Init(out cvTracker, type);
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "CVShutdown")]
        // void CVShutdown(CVObjectTracker* p_object_tracker)
        static extern void CVTracker_Shutdown(IntPtr tracker);

        public void Shutdown()
        {
            CVTracker_Shutdown(cvTracker);
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "CVStartTracking")]
        //bool CVStartTracking(CVObjectTracker* p_object_tracker, unsigned char* image, int width, int height, int roiX, int roiY, int roiW, int roiH)
        static extern bool CVTracker_StartTracking(IntPtr tracker, IntPtr image, int width, int height, int roiX, int roiY, int roiW, int roiH);

        public bool StartTracking(byte[] image, int width, int height, int roiX, int roiY, int roiW, int roiH)
        {
            bool success = true;

            GCHandle pinnedArray = GCHandle.Alloc(image, GCHandleType.Pinned);
            IntPtr imagePtr = pinnedArray.AddrOfPinnedObject();

            if (CVTracker_StartTracking(cvTracker, imagePtr, width, height, roiX, roiY, roiW, roiH))
            {

            }
            else
                success = false;

            pinnedArray.Free();

            return success;
        }


        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "CVUpdate")]
        //bool CVUpdate(CVObjectTracker* p_object_tracker, unsigned char* image, int width, int height, int* pX, int* pY, int* pW, int* pH)
        static extern bool CVTracker_Update(IntPtr tracker, IntPtr image, int width, int height, IntPtr pX, IntPtr pY, IntPtr pW, IntPtr pH);



        public bool Update(byte[] image, int width, int height, ref int roiX, ref int roiY, ref int roiW, ref int roiH)
        {
            bool success = true;

            // Initialize unmanaged memory to hold the array.
            int size = Marshal.SizeOf(image[0]) * image.Length;
            IntPtr imagePtr = Marshal.AllocHGlobal(size);

            // Initialize unmanaged memory to hold coordinates
            int sizeCoordinate = Marshal.SizeOf(roiX);                        
            IntPtr roiXPtr = Marshal.AllocHGlobal(sizeCoordinate);
            IntPtr roiYPtr = Marshal.AllocHGlobal(sizeCoordinate);
            IntPtr roiWPtr = Marshal.AllocHGlobal(sizeCoordinate);
            IntPtr roiHPtr = Marshal.AllocHGlobal(sizeCoordinate);

            try
            {
                // Copy the array to unmanaged memory.
                Marshal.Copy(image, 0, imagePtr, image.Length);

                if (CVTracker_Update(cvTracker, imagePtr, width, height, roiXPtr, roiYPtr, roiWPtr, roiHPtr))
                {
                    // REMOVE THIS...it's only for testing!!! Copy the unmanaged array back to another managed array.
                    //Marshal.Copy(imagePtr, image, 0, image.Length);

                    // copy returned coordinates
                    roiX = Marshal.ReadInt32(roiXPtr);
                    roiY = Marshal.ReadInt32(roiYPtr);
                    roiW = Marshal.ReadInt32(roiWPtr);
                    roiH = Marshal.ReadInt32(roiHPtr);
                }
                else
                {
                    success = false;
                    m_lastErrorMsg = "CVTracker_Update returned false.";
                }
            }
            catch(Exception ex)
            {
                m_lastErrorMsg = ex.Message;
                success = false;
            }
            finally
            {
                // Free the unmanaged memory.
                Marshal.FreeHGlobal(imagePtr);
                Marshal.FreeHGlobal(roiXPtr);
                Marshal.FreeHGlobal(roiYPtr);
                Marshal.FreeHGlobal(roiWPtr);
                Marshal.FreeHGlobal(roiHPtr);
            }

            return success;
        }



        //public bool Update(byte[] image, int width, int height, ref int roiX, ref int roiY, ref int roiW, ref int roiH)
        //{
        //    bool success = true;

        //    GCHandle pinnedArray = GCHandle.Alloc(image, GCHandleType.Pinned);
        //    IntPtr imagePtr = pinnedArray.AddrOfPinnedObject();

        //    GCHandle pinnedRoiX = GCHandle.Alloc(roiX, GCHandleType.Pinned);
        //    IntPtr roiXPtr = pinnedRoiX.AddrOfPinnedObject();

        //    GCHandle pinnedRoiY = GCHandle.Alloc(roiY, GCHandleType.Pinned);
        //    IntPtr roiYPtr = pinnedRoiY.AddrOfPinnedObject();

        //    GCHandle pinnedRoiW = GCHandle.Alloc(roiW, GCHandleType.Pinned);
        //    IntPtr roiWPtr = pinnedRoiW.AddrOfPinnedObject();

        //    GCHandle pinnedRoiH = GCHandle.Alloc(roiH, GCHandleType.Pinned);
        //    IntPtr roiHPtr = pinnedRoiH.AddrOfPinnedObject();


        //    if (CVTracker_Update(cvTracker, imagePtr, width, height, roiXPtr, roiYPtr, roiWPtr, roiHPtr))
        //    {
        //        roiX = Marshal.ReadInt32(roiXPtr);
        //        roiY = Marshal.ReadInt32(roiYPtr);
        //        roiW = Marshal.ReadInt32(roiWPtr);
        //        roiH = Marshal.ReadInt32(roiHPtr);
        //    }
        //    else
        //        success = false;

        //    pinnedRoiX.Free();
        //    pinnedRoiY.Free();
        //    pinnedRoiW.Free();
        //    pinnedRoiH.Free();

        //    pinnedArray.Free();

        //    return success;
        //}

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }

    public class CorrelationTracker : IDisposable
    {

        const string DLL_NAME = "Track.dll";

        private IntPtr correlationTracker = IntPtr.Zero;

        public CorrelationTracker()
        {

        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // IDisposable 

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
        ~CorrelationTracker()
        {
            Dispose(false);
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////




        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "InitObjectTracker")]
        //bool InitObjectTracker(ObjectDetector** pp_object_detector);
        static extern bool ObjectTracker_Init(out IntPtr objectTracker);

        public bool Init()
        {
            return ObjectTracker_Init(out correlationTracker);
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Shutdown")]
        //void Shutdown(ObjectDetector* p_object_tracker);
        static extern void ObjectTracker_Shutdown(IntPtr objectTracker);

        public void Shutdown()
        {
            ObjectTracker_Shutdown(correlationTracker);
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "StartTracking")]
        //bool StartTracking(ObjectDetector* p_object_tracker, unsigned char* image, int width, int height, int roiX, int roiY, int roiW, int roiH);
        static extern bool ObjectTracker_StartTracking(IntPtr objectTracker, IntPtr image, int width, int height, int roiX, int roiY, int roiW, int roiH);

        public bool StartTracking(byte[] image, int width, int height, int roiX, int roiY, int roiW, int roiH)
        {
            bool success = true;

            GCHandle pinnedArray = GCHandle.Alloc(image, GCHandleType.Pinned);
            IntPtr imagePtr = pinnedArray.AddrOfPinnedObject();

            if (ObjectTracker_StartTracking(correlationTracker, imagePtr, width, height, roiX, roiY, roiW, roiH))
            {

            }
            else
                success = false;

            pinnedArray.Free();

            return success;
        }


        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Update")]
        //bool Update(ObjectDetector* p_object_tracker, unsigned char* image, int width, int height, int* pX, int* pY, int* pW, int* pH);
        static extern bool ObjectTracker_Update(IntPtr objectTracker, IntPtr image, int width, int height, IntPtr pX, IntPtr pY, IntPtr pW, IntPtr pH);

        public bool Update(byte[] image, int width, int height, ref int roiX, ref int roiY, ref int roiW, ref int roiH)
        {
            bool success = true;

            GCHandle pinnedArray = GCHandle.Alloc(image, GCHandleType.Pinned);
            IntPtr imagePtr = pinnedArray.AddrOfPinnedObject();

            GCHandle pinnedRoiX = GCHandle.Alloc(roiX, GCHandleType.Pinned);
            IntPtr roiXPtr = pinnedRoiX.AddrOfPinnedObject();

            GCHandle pinnedRoiY = GCHandle.Alloc(roiY, GCHandleType.Pinned);
            IntPtr roiYPtr = pinnedRoiY.AddrOfPinnedObject();

            GCHandle pinnedRoiW = GCHandle.Alloc(roiW, GCHandleType.Pinned);
            IntPtr roiWPtr = pinnedRoiW.AddrOfPinnedObject();

            GCHandle pinnedRoiH = GCHandle.Alloc(roiH, GCHandleType.Pinned);
            IntPtr roiHPtr = pinnedRoiH.AddrOfPinnedObject();


            if (ObjectTracker_Update(correlationTracker, imagePtr, width, height, roiXPtr, roiYPtr, roiWPtr, roiHPtr))
            {
                roiX = Marshal.ReadInt32(roiXPtr);
                roiY = Marshal.ReadInt32(roiYPtr);
                roiW = Marshal.ReadInt32(roiWPtr);
                roiH = Marshal.ReadInt32(roiHPtr);
            }
            else
                success = false;

            pinnedRoiX.Free();
            pinnedRoiY.Free();
            pinnedRoiW.Free();
            pinnedRoiH.Free();

            pinnedArray.Free();

            return success;
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    }
}
