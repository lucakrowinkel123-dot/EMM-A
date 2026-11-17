using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
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
        Check(DetectorScanPolicy.Range(250,150)==150, "definition cap");
        Check(DetectorScanPolicy.Range(1800,24000)==1800, "lowered lidar slider");
        Check(DetectorScanPolicy.Range(-1,24000)==24000, "WeaponCore default sentinel");
        Check(DetectorScanPolicy.Range(-2,150)==0, "invalid lidar range");
        Check(DetectorScanPolicy.Range(100,0)==0, "no usable hardware range");
        Check(DetectorScanPolicy.Range(double.PositiveInfinity,150)==0, "nonfinite reading");
        Check(DetectorScanPolicy.IsSdxLidar("sdx_detectorTargeted_lidar"), "Typhon's installed SDX2 block");
        Check(!DetectorScanPolicy.IsSdxLidar("My Custom Lidar"), "unrelated block");
        var lidar = new CarrierLidar();
        var commander = new TestBlock("LargeProgrammableBlock", 1);
        var vanilla = new TestBlock("LargeOreDetector", 1);
        var otherSorter = new TestSorter("My Custom Lidar", 1);
        var docked = new TestSorter("sdx_detectorTargeted_lidar", 2);
        var blocks = new List<IMyTerminalBlock> { vanilla, otherSorter, docked };
        lidar.Bind(commander, blocks);
        Check(!lidar.Available(7, 1) && !lidar.Available(7, 1, box), "vanilla, arbitrary sorters and a docked lidar cannot activate the plugin");
        foreach (string subtype in new[] { "sdx_detectorTargeted_lidar", "sdx_detectorTargeted_lidarMes" })
        {
            var sensor = new TestSorter(subtype, 1);
            blocks.Add(sensor);
            lidar.Bind(commander, blocks);
            Check(lidar.Available(7, 1) && lidar.Available(7, 1, box), "powered SDX lidar qualifies");
            sensor.Slider.Value -= 0.10f;
            Check(!lidar.Available(7, 1, box), "re-read the live range even for an existing candidate");
            sensor.Slider.Value = -1;
            Check(lidar.Available(7, 1, box), "default WeaponCore range");
            sensor.Enabled = false;
            Check(!lidar.Available(7, 1) && !lidar.Available(7, 1, box), "switching off immediately revokes an existing scan");
            sensor.Enabled = true;
            sensor.IsWorking = false;
            Check(!lidar.Available(7, 1), "no power or damaged");
            sensor.IsWorking = true;
            Check(!lidar.Available(8, 1) && !lidar.Available(7, 2), "access or carrier changed");
            Check(lidar.Available(7, 0) && !lidar.Available(7, 0, box), "out-of-seat services cannot scan");
            commander.CubeGrid.EntityId = 3;
            Check(!lidar.Available(7, 0), "out-of-seat services also require the lidar on the carrier after a grid split");
            commander.CubeGrid.EntityId = 1;
            sensor.Slider = null;
            Check(!lidar.Available(7, 1, box), "absent range control gives no scan");
            sensor.CubeGrid.EntityId = 2;
            Check(!lidar.Available(7, 1), "detached block immediately stops qualifying");
            sensor.CubeGrid.EntityId = 1;
            sensor.MarkedForClose = true;
            Check(!lidar.Available(7, 1), "removed block");
            blocks.Remove(sensor);
        }
        Console.WriteLine("PASS lidar-only activation, live power, ownership and sphere-to-box range checks");
    }
}
