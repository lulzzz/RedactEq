#include "stdafx.h"

#define DllExport  extern "C" __declspec( dllexport ) 

typedef intptr_t ItemListHandle;

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


//////////////////////////////////////////////////////////////////////////////////////
//////////////////////////////////////////////////////////////////////////////////////
//////////////////////////////////////////////////////////////////////////////////////
//
// Non Maximum Suppression (NMS) Interface

DllExport bool Init_NonMaxSuppression(NonMaximumSuppression** pp_nms)
{
	ready = true;
	
	*pp_nms = new NonMaximumSuppression();

	return ready;
}

DllExport void Shutdown_NonMaxSuppression(NonMaximumSuppression* p_nms)
{
	delete p_nms;
}


DllExport bool PerformNMS(NonMaximumSuppression* p_nms, BoundingBox const* pBboxes, int boxCount, 
							float threshold, BoundingBox** pData, int* count)
{
	if (ready)
	{
		std::vector<BoundingBox> bboxes(pBboxes, pBboxes + boxCount);

		std::vector<BoundingBox>* pOutBoxes = p_nms->nms(bboxes, threshold);

		*pData = (BoundingBox*)malloc(sizeof(BoundingBox) * pOutBoxes->size());
		memcpy(*pData, (void*)pOutBoxes->data(), sizeof(BoundingBox)*pOutBoxes->size());

		*count = pOutBoxes->size();

		pOutBoxes->clear();
		delete(pOutBoxes);
	}
	return ready;
}

DllExport void ReleaseNMS(BoundingBox* pData)
{	
	delete pData;
}



//////////////////////////////////////////////////////////////////////////////////////
//////////////////////////////////////////////////////////////////////////////////////
//////////////////////////////////////////////////////////////////////////////////////
//
// DLIB Correlation Tracker Interface


extern "C" DllExport bool InitObjectTracker(CorrelationTracker** pp_object_tracker)
{
	bool ready = true;
	*pp_object_tracker = new CorrelationTracker();

	if (*pp_object_tracker == 0)
		ready = false;

	return ready;
}


extern "C" DllExport void Shutdown(CorrelationTracker* p_object_tracker)
{
	delete p_object_tracker;
}



extern "C" DllExport bool StartTracking(CorrelationTracker* p_object_tracker, unsigned char* image, int width, int height, int roiX, int roiY, int roiW, int roiH)
{
	bool success = true;

	success = p_object_tracker->StartTracking((rgb_pixel*)image, width, height, roiX, roiY, roiW, roiH);

	return success;
}


extern "C" DllExport bool Update(CorrelationTracker* p_object_tracker, unsigned char* image, int width, int height, int* pX, int* pY, int* pW, int* pH)
{
	bool success = true;

	success = p_object_tracker->Update((rgb_pixel*)image, width, height, pX, pY, pW, pH);

	return success;
}
