#include "Tools.h"



CudaTools::CudaTools()
{

}

CudaTools::~CudaTools()
{

}



__global__ void redact_areas(uchar3* imageIn, uchar3* imageOut, int width, int height, int4* rects, int num_rects, int block_size)
{
	// This function attempts to obscure (redact) areas of an image defined by the rects passed in, i.e. pixels inside the rects
	// are "redacted".  
	// The redaction process simply covers a specified rectangle with square blocks of solid color.  The size of these blocks
	// is set by the parameter block_size (in pixels).  The color of each block it taken from the center pixel of each block; every
	// pixel inside the block is set to this color.
	//
	// For example, given a rectangle of 10,10,50,80 (x,y,w,h format) and a block size of 8.  There will be at least a 5 x 9 blocks 
	// created to cover this region.  There may actually be a few more, as some padding around the edges is added.
	//
	// Parameters:
	//		image = the image data to work on
	//		width, height = the pixel dimension of the data
	//		rects = pointer to a array of rectangles (data packed into an int4 struct)
	//		num_rects = number of rectangles in the array above
	//		block_size = size of the blocks in pixels (typical would be 8 or 16)


	// calc x,y position of pixel to operate on
	uint32_t x = blockIdx.x * blockDim.x + threadIdx.x; // column of pixel inside panel
	uint32_t y = blockIdx.y * blockDim.y + threadIdx.y; // row of pixel inside panel

														// make sure we don't try to operate outside the image
	if (x >= width) return;
	if (y >= height) return;

	bool redact = false;

	// test to see if this pixel is inside one of the rects
	for (int i = 0; i < num_rects; i++)
	{
		int xr = rects[i].x;
		int yr = rects[i].y;
		int wr = rects[i].z;
		int hr = rects[i].w;

		if (x >= xr && y >= yr && x < (xr + wr) && y < (yr + hr))
		{
			redact = true;

			// redaction area origin
			int rao_x = xr - block_size / 2;
			int rao_y = yr - block_size / 2;
			if (rao_x < 0) rao_x = 0;
			if (rao_y < 0) rao_y = 0;

			// number of rows,cols of redaction blocks
			//int num_blocks_x = (wr + block_size) / (block_size)+1;
			//int num_blocks_y = (hr + block_size) / (block_size)+1;

			// redaction block row and col
			int rb_row = (y - rao_y) / block_size;
			int rb_col = (x - rao_x) / block_size;

			// redaction block center (this is where we get the color used for all pixels in this redaction block)
			int rbc_x = rao_x + (rb_col * block_size) + (block_size / 2);
			int rbc_y = rao_y + (rb_row * block_size) + (block_size / 2);
			if (rbc_x >= width) rbc_x = width - 1;
			if (rbc_y >= height) rbc_y = height - 1;

			// get the color to be set for this entire redaction block
			uchar3 color = imageIn[rbc_y * width + rbc_x];

			// set the color of this pixel
			imageOut[y * width + x] = color;

			// can't also be inside any other window, so break out of for loop
			break;
		}
	}

	if (!redact) // not inside any of the redaction rectangles, so just copy the pixel from imageIn to imageOut
	{
		imageOut[y*width + x] = imageIn[y*width + x];
	}
}


void CudaTools::RedactAreas(uchar3* rgb_image_in, uchar3* rgb_image_out, int width, int height, int4* rects, int num_rects, int block_size)
{
	dim3 block, grid;
	block.x = 32; block.y = 16; block.z = 1;

	grid.x = (width + block.x - 1) / block.x;
	grid.y = (height + block.y - 1) / block.y;
	grid.z = 1;

	redact_areas << <grid, block >> > (rgb_image_in, rgb_image_out, width, height, rects, num_rects, block_size);
}

