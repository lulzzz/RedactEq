#include "CorrelationTracker.h"



CorrelationTracker::CorrelationTracker()
{
}


CorrelationTracker::~CorrelationTracker()
{
}


bool CorrelationTracker::StartTracking(rgb_pixel imageRgb[], int width, int height, int roiX, int roiY, int roiW, int roiH)
{
	bool success = true;

	std::ostringstream strs;
	strs << "Start Tracking: " << roiX << "," << roiY << "," << roiW << "," << roiH << "\n";
	std::string str = strs.str();
	OutputDebugString(str.c_str());

	dlib::matrix<rgb_pixel> matrix = mat(imageRgb, height, width, width);

	m_tracker.start_track(matrix, drectangle(roiX, roiY, roiX + roiW - 1, roiY + roiH - 1));

	return success;
}



bool CorrelationTracker::Update(rgb_pixel imageRgb[], int width, int height, int* pX, int* pY, int* pW, int* pH)
{
	bool success = true;

	dlib::matrix<rgb_pixel> matrix = mat(imageRgb, height, width, width);

	//Returns the peak to side - lobe ratio.This is a number that measures howR
	//	confident the tracker is that the object is inside #get_position().
	//	Larger values indicate higher confidence.
	double score = m_tracker.update(matrix);

	dlib::drectangle rect = m_tracker.get_position();

	*pX = (int)rect.left();
	*pY = (int)rect.top();
	*pW = (int)(rect.right() - rect.left() + 1);
	*pH = (int)(rect.bottom() - rect.top() + 1);

	std::ostringstream strs;
	strs << "Update: " << score << ": " << *pX << "," << *pY << "," << *pW << "," << *pH << "\n";
	std::string str = strs.str();
	OutputDebugString(str.c_str());

	return success;
}

