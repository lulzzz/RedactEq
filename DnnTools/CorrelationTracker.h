#pragma once

#include "Common.h"

#include <dlib/dir_nav.h>
#include "dlib\image_processing.h"
#include "dlib\image_io.h"
#include "dlib\data_io.h"

using namespace dlib;

class CorrelationTracker
{
public:
	CorrelationTracker();
	~CorrelationTracker();

	bool StartTracking(rgb_pixel imageRgb[], int width, int height, int roiX, int roiY, int roiW, int roiH);

	bool Update(rgb_pixel imageRgb[], int width, int height, int* pX, int* pY, int* pW, int* pH);

	correlation_tracker m_tracker;
};

