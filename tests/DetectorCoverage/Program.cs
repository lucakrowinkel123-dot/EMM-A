using System;
using EMMA3.Client;
using VRageMath;
class Program
{
    static void Check(bool pass, string name) { if (!pass) throw new Exception(name); }
    static void Main()
    {
        var box = new BoundingBoxD(new Vector3D(1000,1000,1000), new Vector3D(7000,7000,7000));
        Check(DetectorScanPolicy.Overlaps(new Vector3D(850.05,1100,1100),150,box), "5 cm overlap must admit the whole asteroid");
        Check(!DetectorScanPolicy.Overlaps(new Vector3D(849.95,1100,1100),150,box), "5 cm gap");
        Check(DetectorScanPolicy.Overlaps(new Vector3D(850,1100,1100),150,box), "tangent");
        Check(!DetectorScanPolicy.Overlaps(new Vector3D(890,890,1100),150,box), "outside a spherical corner");
        Check(!DetectorScanPolicy.Overlaps(box.Center,0,box), "absent detector");
        Check(!DetectorScanPolicy.Overlaps(box.Center,double.NaN,box), "invalid range");
        Check(DetectorScanPolicy.Range(45,150,false)==45, "configured ore detector metres");
        Check(DetectorScanPolicy.Range(250,150,false)==150, "definition cap");
        Check(DetectorScanPolicy.Range(1800,24000,true)==1800, "lowered lidar slider");
        Check(DetectorScanPolicy.Range(-1,24000,true)==24000, "WeaponCore default sentinel");
        Check(DetectorScanPolicy.Range(-1,150,false)==0, "invalid ore range");
        Check(DetectorScanPolicy.Range(100,0,true)==0, "no usable hardware range");
        Check(DetectorScanPolicy.Range(double.PositiveInfinity,150,false)==0, "nonfinite reading");
        Check(DetectorScanPolicy.IsSdxLidar("sdx_detectorTargeted_lidar"), "Typhon's installed SDX2 block");
        Check(!DetectorScanPolicy.IsSdxLidar("My Custom Lidar"), "unrelated block");
        Console.WriteLine("PASS detector range and sphere-to-box boundary checks");
    }
}
