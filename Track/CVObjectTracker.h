#pragma once
 
#include <opencv2/core/utility.hpp>
#include <opencv2/tracking.hpp>
#include <opencv2/videoio.hpp>
#include <opencv2/highgui.hpp>

#include <iostream>
#include <cstring>
#include <ctime>
#include <vector>

using namespace cv;
using namespace std;



class CVObjectTracker
{
public:
	CVObjectTracker();
	~CVObjectTracker();

	bool StartTracking(uint8_t imageRgb[], int width, int height, int roiX, int roiY, int roiW, int roiH);

	bool Update(uint8_t image[], int width, int height, int* pX, int* pY, int* pW, int* pH);

	bool createTrackerByName(string trackerType);

	Ptr<Tracker> mp_tracker;
	vector<Rect2d> m_objects;
	vector<Rect> m_ROIs;

};

