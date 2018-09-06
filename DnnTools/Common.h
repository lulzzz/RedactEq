#pragma once


enum PointInRectangle { XMIN, YMIN, XMAX, YMAX };

struct Point
{
	float x;
	float y;
	Point(float X, float Y)
	{
		x = X;
		y = Y;
	}
};


struct Rect
{
	float x1;
	float y1;
	float x2;
	float y2;
	Rect(Point p1, Point p2)
	{
		if (p1.x < p2.x)
		{
			x1 = p1.x;
			x2 = p2.x;
		}
		else
		{
			x1 = p2.x;
			x2 = p1.x;
		}
		if (p1.y < p2.y)
		{
			y1 = p1.y;
			y2 = p2.y;
		}
		else
		{
			y1 = p2.y;
			y2 = p1.y;
		}
	}
};

#pragma pack(push, 1)
struct BoundingBox
{
	float cx; // centroid x coordinate
	float cy; // centroid y coordinate
	float x1;
	float y1;
	float x2;
	float y2;
	float confidence;
	int classID; // this is the id of class in the class/label collection for the neural net
	int objectID; // this is a unique id of object detected.  This is used by the Tracker (if being used)
	BoundingBox(float X1, float Y1, float X2, float Y2, int ClassID, int ObjectID, float Confidence)
	{
		cx = (X1 + X2) / 2.0f;
		cy = (Y1 + Y2) / 2.0f;
		x1 = X1;
		y1 = Y1;
		x2 = X2;
		y2 = Y2;
		classID = ClassID;
		objectID = ObjectID;
		confidence = Confidence;
	}
};
#pragma pack(pop)
