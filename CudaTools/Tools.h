#pragma once

#include <stdint.h>
#include <math.h>
#include <cuda_runtime.h>
#include <cuda.h>
#include <stdio.h>
#include <string.h>


class CudaTools
{
public:
	CudaTools();
	~CudaTools();

	void RedactAreas(uchar3* rgb_image_in, uchar3* rgb_image_out, int width, int height, int4* rects, int num_rects, int block_size);

};