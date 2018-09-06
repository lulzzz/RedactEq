#define DllExport   __declspec( dllexport ) 

#include "stdafx.h"

#include "ObjectTracker.h"
#include "CVObjectTracker.h"


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



extern "C" DllExport bool CVInitTracker(CVObjectTracker** pp_object_tracker, const char* trackerType)
{
	bool ready = true;
	*pp_object_tracker = new CVObjectTracker();
	ready = (*pp_object_tracker)->createTrackerByName(trackerType);

	return ready;
}


extern "C" DllExport void CVShutdown(CVObjectTracker* p_object_tracker)
{
	delete p_object_tracker;
}



extern "C" DllExport bool CVStartTracking(CVObjectTracker* p_object_tracker, unsigned char* image, int width, int height, int roiX, int roiY, int roiW, int roiH)
{
	bool success = true;
	
	success = p_object_tracker->StartTracking((uint8_t*)image, width, height, roiX, roiY, roiW, roiH);
		
	return success;
}


extern "C" DllExport bool CVUpdate(CVObjectTracker* p_object_tracker, unsigned char* image, int width, int height, int* pX, int* pY, int* pW, int* pH)
{
	bool success = true;
	
	success = p_object_tracker->Update((uint8_t*)image, width, height, pX, pY, pW, pH);
		
	return success;
}




//
//extern "C" DllExport bool InitObjectTracker(ObjectTracker** pp_object_tracker)
//{
//	bool ready = true;
//	*pp_object_tracker = new ObjectTracker();
//	
//	if (*pp_object_tracker == 0)
//		ready = false;
//
//	return ready;
//}
//
//
//extern "C" DllExport void Shutdown(ObjectTracker* p_object_tracker)
//{
//	delete p_object_tracker;
//}
//
//
//
//extern "C" DllExport bool StartTracking(ObjectTracker* p_object_tracker, unsigned char* image, int width, int height, int roiX, int roiY, int roiW, int roiH)
//{
//	bool success = true;
//	
//	success = p_object_tracker->StartTracking((rgb_pixel*)image, width, height, roiX, roiY, roiW, roiH);
//		
//	return success;
//}
//
//
//extern "C" DllExport bool Update(ObjectTracker* p_object_tracker, unsigned char* image, int width, int height, int* pX, int* pY, int* pW, int* pH)
//{
//	bool success = true;
//	
//	success = p_object_tracker->Update((rgb_pixel*)image, width, height, pX, pY, pW, pH);
//		
//	return success;
//}