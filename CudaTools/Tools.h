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

	uchar3* RedactAreas(uchar3* bgr_image_in, int width, int height, int4* rects, int num_rects, int block_size);	

};