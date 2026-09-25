namespace Probe;

public static class Shapes
{
    public static double Perimeter(int side)
    {
        if (side < 0) return 0;
        return 4 * side;
    }

    public static double Area(int side)
    {
        if (side < 0) return 0;
        return side * side;
    }

    public static double Area(double radius)
    {
        if (radius < 0) return 0;
        return 3.14 * radius * radius;
    }
}
