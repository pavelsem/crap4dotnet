namespace Probe;

public static class Twins
{
    public static int A(int x)
    {
        return Helper(x);

        static int Helper(int v)
        {
            if (v > 0) return v;
            return -v;
        }
    }

    public static int B(int x)
    {
        return Helper(x) + 1;

        static int Helper(int v)
        {
            if (v > 1) return v;
            if (v > 2) return v;
            return 0;
        }
    }
}
