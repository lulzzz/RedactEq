#include "CVObjectTracker.h"



CVObjectTracker::CVObjectTracker()
{

}


CVObjectTracker::~CVObjectTracker()
{
	
}

bool CVObjectTracker::StartTracking(uint8_t imageRgb[], int width, int height, int roiX, int roiY, int roiW, int roiH)
{
	Mat newImg = Mat(height, width, CV_8UC3, imageRgb);
	Rect2d rect = { (double)roiX, (double)roiY, (double)roiW, (double)roiH };
	bool success =  mp_tracker->init(newImg, rect);
		
	return success;	
}


bool CVObjectTracker::Update(uint8_t image[], int width, int height, int* pX, int* pY, int* pW, int* pH)
{	
	Mat newImg = Mat(height, width, CV_8UC3, image);
	Rect2d rect;
	
	bool success = mp_tracker->update(newImg, rect);

	if (success)
	{
		*pX = (int)rect.x;
		*pY = (int)rect.y;
		*pW = (int)rect.width;
		*pH = (int)rect.height;
	}

	
	rectangle(newImg, Point(rect.x,rect.y), Point(rect.x+rect.width-1,rect.y+rect.height-1),Scalar(255,0,0));
	//imshow("display", img2);
	//waitKey(0);

	return success;
}


bool CVObjectTracker::createTrackerByName(string trackerType) 
{
	bool success = true;

	try
	{		
		if (strcmp(trackerType.c_str(), "BOOSTING") == 0) {
			mp_tracker = TrackerBoosting::create();
		}
		else if (strcmp(trackerType.c_str(), "CSRT") == 0) {
			mp_tracker = TrackerCSRT::create();
		}
		else if (strcmp(trackerType.c_str(), "MOSSE") == 0) {
			mp_tracker = TrackerMOSSE::create();
		}
		//else if (strcmp(trackerType.c_str(), "MIL") == 0) {
		//	mp_tracker = TrackerMIL::create();
		//}
		else if (strcmp(trackerType.c_str(), "KCF") == 0) {
			mp_tracker = TrackerKCF::create();
		}
		else if (strcmp(trackerType.c_str(), "TLD") == 0) {
			mp_tracker = TrackerTLD::create();
		}
		//else if (strcmp(trackerType.c_str(), "MEDIANFLOW") == 0) {
		//	mp_tracker = TrackerMedianFlow::create();
		//}
		//else if (strcmp(trackerType.c_str(), "GOTURN") == 0) {
		//	mp_tracker = TrackerGOTURN::create();
		//}
		//else {
		//	cout << "Incorrect tracker name" << endl;
		//	cout << "Available trackers are: " << endl;
		//	cout << "BOOSTING" << endl;
		//	cout << "CRST" << endl;
		//	cout << "MIL" << endl;
		//	cout << "KCF" << endl;
		//	cout << "TLD" << endl;
		//	cout << "MEDIANFLOW" << endl;
		//	cout << "GOTURN" << endl;
		//	success = false;
		//}
	}
	catch (Exception)
	{
		success = false;
	}
	
	return success;
}