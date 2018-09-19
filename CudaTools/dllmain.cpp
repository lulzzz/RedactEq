#include "stdafx.h"

#define DllExport  extern "C" __declspec( dllexport ) 

bool ready = false;


BOOL APIENTRY DllMain(HMODULE hModule,
	DWORD  ul_reason_for_call,
	LPVOID lpReserved)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}



DllExport bool InitCudaTools(CudaTools** pp_CudaTools)
{	
	*pp_CudaTools = new CudaTools();

	return ready;
}



DllExport void Shutdown_CudaTools(CudaTools* p_CudaTools)
{
	delete p_CudaTools;
}


DllExport uchar3* RedactAreas(CudaTools* p_CudaTools, uchar3* imageIn, int width, int height, int4* rects, int numRects, int blockSize)
{
	return p_CudaTools->RedactAreas(imageIn, width, height, rects, numRects, blockSize);
}

DllExport void ReleaseMemory(void* ptr)
{
	free(ptr);
}