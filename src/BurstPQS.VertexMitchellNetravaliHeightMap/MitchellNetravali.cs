namespace BurstPQS.Niako;

readonly struct MitchellNetravali
{
    private readonly double C;

    private readonly double _n6BnC;
    private readonly double _n32BnC2;
    private readonly double _32BCn2;
    private readonly double _6BC;
    private readonly double _2B2C;
    private readonly double _2BCn3;
    private readonly double _n52Bn2C3;
    private readonly double _n2BnC;
    private readonly double _2BC;
    private readonly double _6B;
    private readonly double _n3B1;

    public MitchellNetravali(double B, double C)
    {
        this.C = C;

        _n6BnC = (-1 / 6.0) * B - C;
        _n32BnC2 = (-3 / 2.0) * B - C + 2;
        _32BCn2 = -_n32BnC2;
        _6BC = -_n6BnC;
        _2B2C = 0.5f * B + 2 * C;
        _2BCn3 = 2 * B + C - 3;
        _n52Bn2C3 = (-5 / 2.0) * B - 2 * C + 3;
        _n2BnC = -0.5f * B - C;
        _2BC = -_n2BnC;
        _6B = (1 / 6.0) * B;
        _n3B1 = (-1 / 3.0) * B + 1;
    }

    public double Evaluate(double P0, double P1, double P2, double P3, double d)
    {
        return (_n6BnC * P0 + _n32BnC2 * P1 + _32BCn2 * P2 + _6BC * P3) * d * d * d
            + (_2B2C * P0 + _2BCn3 * P1 + _n52Bn2C3 * P2 - C * P3) * d * d
            + (_n2BnC * P0 + _2BC * P2) * d
            + _6B * P0
            + _n3B1 * P1
            + _6B * P2;
    }
}
