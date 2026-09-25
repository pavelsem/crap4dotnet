namespace Probe;

public static class Gate
{
    public static int Stable(int x)
    {
        if (x == 1) return 1;
        if (x == 2) return 2;
        if (x == 3) return 3;
        if (x == 4) return 4;
        if (x == 5) return 5;
        return 0;
    }

    public static int Covered(int x)
    {
        if (x == 1) return 1;
        return 0;
    }

    public static int Improver(int x)
    {
        if (x == 1) return 1;
        if (x == 2) return 2;
        if (x == 3) return 3;
        if (x == 4) return 4;
        if (x == 5) return 5;
        // spare
        return 0;
    }

    public static int Crosser(int x)
    {
        if (x == 1) return 1;
        if (x == 2) return 2;
        if (x == 3) return 3;
        if (x == 4) return 4;
        if (x == 5) return 5;
        return 0;
    }

    public static int OldMonster(int x)
    {
        if (x == 1) return 1;
        if (x == 2) return 2;
        if (x == 3) return 3;
        if (x == 4) return 4;
        if (x == 5) return 5;
        if (x == 6) return 6;
        return 0;
    }

    public static int Fresh(int x)
    {
        if (x == 1) return 1;
        if (x == 2) return 2;
        if (x == 3) return 3;
        if (x == 4) return 4;
        if (x == 5) return 5;
        return 0;
    }
}
