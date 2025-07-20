#define FLT_MAX  3.4028235e+38
#define FLT_MIN -3.4028235e+38
#define SQRT2 1.4142135623730950488016887242097

static const int2 offsets[8] =
{
    int2(-1, 0),
    int2(1, 0),
    int2(0, -1),
    int2(0, 1),
    int2(-1, -1),
    int2(1, 1),
    int2(-1, 1),
    int2(1, -1)
};

static const float costs[8] =
{
    1,
    1,
    1,
    1,
    SQRT2,
    SQRT2,
    SQRT2,
    SQRT2
};

static const uint numberOfDirections[2] =
{
    4,
    8
};

int flatten(uint x, uint y, uint width)
{
    return x + y * width;
}