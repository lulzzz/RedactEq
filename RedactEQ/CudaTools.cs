using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace RedactEQ
{
    class CudaTools : IDisposable
    {

        private IntPtr cudaTools = IntPtr.Zero;
        const string DLL_NAME = "CudaTools.dll";

        // constructor
        public CudaTools()
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
        ~CudaTools()
        {
            Dispose(false);
        }

        #endregion

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Shutdown_CudaTools")]
        // void Shutdown_CudaTools(CudaTools* p_CudaTools)
        static extern void CudaTools_Shutdown(IntPtr pCudaTools);

        public void Shutdown()
        {
            try
            {
                CudaTools_Shutdown(cudaTools);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Marshaling Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "InitCudaTools")]
        //  bool InitCudaTools(CudaTools** pp_CudaTools)
        static extern bool CudaTools_Init(out IntPtr pCudaTools);

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        public bool Init()
        {
            bool initialized = false;
            cudaTools = new IntPtr(0);
            try
            {
                CudaTools_Init(out cudaTools);
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

        

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "RedactAreas")]
        // uchar3* RedactAreas(CudaTools* p_CudaTools, uchar3* imageIn, int width, int height, int4* rects, int numRects, int blockSize)
        static extern IntPtr CudaTools_RedactAreas(IntPtr pCudaTools, IntPtr imageIn, int width, int height, IntPtr pRects, int numRects, int blockSize);

        public void RedactAreas_3(byte[] imageIn, int width, int height, ObservableCollection<VideoTools.FrameEdit> boxList, int blockSize, out byte[] imageOut)
        {
            try
            {
                if (boxList.Count > 0)
                {
                    GCHandle pinnedImageIn = GCHandle.Alloc(imageIn, GCHandleType.Pinned);
                    IntPtr imageInPtr = pinnedImageIn.AddrOfPinnedObject();

                    int[] rects = new int[boxList.Count * 4];
                    int ndx = 0;
                    foreach (VideoTools.FrameEdit fe in boxList)
                    {
                        rects[ndx + 0] = fe.box.x1;
                        rects[ndx + 1] = fe.box.y1;
                        rects[ndx + 2] = fe.box.x2 - fe.box.x1 + 1;
                        rects[ndx + 3] = fe.box.y2 - fe.box.y1 + 1;

                        ndx += 4;
                    }
                    GCHandle pinnedRects = GCHandle.Alloc(rects, GCHandleType.Pinned);
                    IntPtr pRects = pinnedRects.AddrOfPinnedObject();

                    IntPtr outPtr = IntPtr.Zero;
                    outPtr = CudaTools_RedactAreas(cudaTools, imageInPtr, width, height, pRects, boxList.Count, blockSize);

                    imageOut = new byte[width * height * 3];
                    Marshal.Copy(outPtr, imageOut, 0, width * height * 3);

                    pinnedImageIn.Free();
                    pinnedRects.Free();

                    CudaTools_ReleaseMemory(outPtr);

                }
                else
                {
                    imageOut = new byte[width * height * 3];
                    Buffer.BlockCopy(imageIn, 0, imageOut, 0, width * height * 3);
                }

               
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Marshaling Error", MessageBoxButton.OK, MessageBoxImage.Error);
                imageOut = null;
            }
        }


        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ReleaseMemory")]
        // DllExport void ReleaseMemory(void* ptr)
        static extern void CudaTools_ReleaseMemory(IntPtr ptr);

        // no public interface method defined because this is only used inside this class



    }
}
