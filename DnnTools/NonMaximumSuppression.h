#include <vector>
#include <numeric>
#include <algorithm>
#include "Common.h"

using std::vector;



class NonMaximumSuppression
{
public:
	NonMaximumSuppression();
	~NonMaximumSuppression();

	vector<BoundingBox>* nms(vector < BoundingBox> & boxes, const float & threshold);

	std::vector<Rect> nms(const std::vector<std::vector<float>> &,
		const float &);

	std::vector<float> GetPointFromRect(const std::vector<std::vector<float>> &,
		const PointInRectangle &);

	std::vector<float> GetPointFromBoundingBox(const vector<BoundingBox> & boxes, const PointInRectangle & pos);

	std::vector<float> ComputeArea(const std::vector<float> &,
		const std::vector<float> &,
		const std::vector<float> &,
		const std::vector<float> &);

	template <typename T>
	std::vector<int> argsort(const std::vector<T> & v);

	std::vector<float> Maximum(const float &,
		const std::vector<float> &);

	std::vector<float> Minimum(const float &,
		const std::vector<float> &);

	std::vector<float> CopyByIndexes(const std::vector<float> &,
		const std::vector<int> &);

	std::vector<int> RemoveLast(const std::vector<int> &);

	std::vector<float> Subtract(const std::vector<float> &,
		const std::vector<float> &);

	std::vector<float> Multiply(const std::vector<float> &,
		const std::vector<float> &);

	std::vector<float> Divide(const std::vector<float> &,
		const std::vector<float> &);

	std::vector<int> WhereLarger(const std::vector<float> &,
		const float &);

	std::vector<int> RemoveByIndexes(const std::vector<int> &,
		const std::vector<int> &);

	std::vector<Rect> BoxesToRectangles(const std::vector<std::vector<float>> &);

	template <typename T>
	std::vector<T> FilterVector(const std::vector<T> &, const std::vector<int> &);

	template <typename T>
	vector<T>* FilterVectorP(const vector<T> & vec, const vector<int> & idxs);


};



