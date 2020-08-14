using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace EVGAProxy
{
    public class EVGA64Proxy
    {
        const int NUM_GPUS = 4;
        const bool SLI_BRIDGE = true;
        private static string ProcName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;
        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), $"evgaproxy.{ProcName}.log"), DateTime.Now.ToString() + ": " + (msg ?? "") + "\r\n");
            }
            catch {
                System.Threading.Thread.Sleep(100);
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), $"evgaproxy.{ProcName}.log"), DateTime.Now.ToString() + ": " + (msg ?? "") + "\r\n");
                }
                catch { }
            }
        }
        private string _evgaPath = null;
        private string _precisionExe = null;
        private string _precisionExeName = null;
        private Type _ledInterface;
        private PropertyInfo _zoneSupported;
        private PropertyInfo _zoneSet;
        private MethodInfo _setLEDOff;
        private MethodInfo _setLEDStaticOn;
        private MethodInfo _getDefaultColor;
        private List<object> _instances = new List<object>();
        public EVGA64Proxy()
        {
            try
            {
                string assyPath = null;
                string rootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                try
                {
                    RegistryKey key = null;
                    try
                    {
                        key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)?.OpenSubKey(@"SOFTWARE\EVGA Precision X1");
                    }
                    catch { }
                    if (key == null)
                    {
                        try
                        {
                            key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)?.OpenSubKey(@"SOFTWARE\EVGA Precision X1");
                        }
                        catch
                        { }
                    }
                    if (key == null)
                    {
                        //todo: bad
                        throw new Exception("EVGA Precision X1 doesn't seem to be installed (no key)");
                    }
                    var dirval = key.GetValue(@"Install_Dir")?.ToString();
                    if (string.IsNullOrWhiteSpace(dirval))
                    {
                        //todo: bad
                        throw new Exception("EVGA Precision X1 doesn't seem to be installed (no installdir)");
                    }
                    _evgaPath = dirval;
                    var exeName = "PrecisionX_x64";
                    assyPath = Path.Combine(_evgaPath, exeName + ".exe");
                    if (!File.Exists(assyPath))
                    {
                        exeName = "PrecisionX";
                        assyPath = Path.Combine(_evgaPath, exeName + ".exe");
                        if (!File.Exists(assyPath))
                        {
                            throw new Exception("PrecisionX.exe or PrecisionX_x64.exe don't seem to exist");
                        }

                    }
                    _precisionExeName = exeName;
                    _precisionExe = assyPath;
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                    //fail
                    throw;
                }
                var dlls = Directory.GetFiles(_evgaPath, "*.dll").ToList();
                foreach (var dll in dlls)
                {
                    try
                    {
                        File.Copy(dll, Path.Combine(rootPath, Path.GetFileName(dll)), true);
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed copying {dll}: {ex.Message}");
                    }
                }
                //var nvFile = Path.Combine(_evgaPath, "ManagedNvApi.dll");
                
                //if (!File.Exists(nvFile))
                //{
                //    throw new Exception("Unable to find ManagedNvApi.dll");
                //}

                //try
                //{
                //    Log("Finding ManagedNvApi");
                //    Log(Directory.GetCurrentDirectory());
                //    var newFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Path.GetFileName(nvFile));
                //    bool copy = false;
                //    if (File.Exists(newFile))
                //    {
                //        //comparing file sizes should be enough?
                //        if (new FileInfo(nvFile).Length != new FileInfo(newFile).Length)
                //        {
                //            copy = true;

                //        }
                //    }
                //    else
                //    {
                //        copy = true;
                //    }
                //    if (copy)
                //    {
                //        Log("copying managednvapi to " + newFile);
                //        File.Copy(nvFile, newFile, true);
                //    }
                //}
                //catch (Exception ex)
                //{
                //    //failed to copy file
                //    throw new Exception("Failed to copy ManagedNvApi locally! " + ex.Message, ex);
                //}
                var curDir = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(_evgaPath);
                AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
                try
                {
                    Assembly evgaAssembly = null;

                    try
                    {
                        evgaAssembly = Assembly.Load(_precisionExeName);
                    }
                    catch (ReflectionTypeLoadException fle)
                    {
                        foreach (var g in fle.LoaderExceptions)
                        {
                            Log(g.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Failed to extract EVGA stuff", ex);
                    }

                    var ledInterface = evgaAssembly.GetType("PX18.Model.ILedCtrl");
                    if (ledInterface == null)
                    {
                        throw new Exception("Failed to find ILedCtrl interface");
                    }
                    _ledInterface = ledInterface;
                    _zoneSet = _ledInterface.GetProperty("ZoneSet");
                    _setLEDOff = _ledInterface.GetMethod("SetLEDOff");
                    _setLEDStaticOn = _ledInterface.GetMethod("SetLEDStaticOn");
                    _getDefaultColor = _ledInterface.GetMethod("GetDefaultColor");

                    _zoneSupported = ledInterface.GetProperty("ZoneSupported");

                    List<Type> ledControls;
                    try
                    {
                        ledControls = evgaAssembly.GetTypes().Where(x => x.IsClass && ledInterface.IsAssignableFrom(x)).ToList();
                    }
                    catch (ReflectionTypeLoadException fle)
                    {
                        foreach (var g in fle.LoaderExceptions)
                        {
                            Log(g.Message);
                        }
                        throw;
                    }
                    //control type, gpu index, is sli bridge
                    Action<int, bool> tryAddControlInstances = (gpuIdx, isSliBridge) =>
                    {
                        foreach (var control in ledControls)
                        {
                            Log($"Trying to init {control.Name} with gpu index {gpuIdx} and is sli bridge: {isSliBridge}");
                            var initializedProp = control.GetProperty("IsInitialized");
                            if (initializedProp == null)
                            {
                                //doesn't have IsInitialized
                                continue;
                            }
                            object instance = null;
                            try
                            {

                                var con = control.GetConstructor(new Type[] { typeof(uint), typeof(bool) });
                                if (con == null)
                                {
                                    con = control.GetConstructor(new Type[] { typeof(uint) });
                                    if (con == null)
                                    {
                                        //type doesn't have an expected constructor
                                        continue;
                                    }
                                    else
                                    {
                                        if (isSliBridge)
                                        {
                                            Log($"{control.Name} doesn't support SLI bridge, skipping for isSliBridge: {isSliBridge}");
                                            continue;
                                        }
                                        instance = Activator.CreateInstance(control, new object[] { (uint)gpuIdx });
                                    }
                                }
                                else
                                {
                                    instance = Activator.CreateInstance(control, new object[] { (uint)gpuIdx, isSliBridge });
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"Exception trying to create instance of {control.Name} with gpu index {gpuIdx} and is sli bridge: {isSliBridge}: {ex.Message} {ex.StackTrace}");
                                //exception creating instance
                            }
                            if (instance == null)
                            {
                                Log($"No instance of {control.Name} was created with gpu index {gpuIdx} and is sli bridge: {isSliBridge}");
                                //failed to create instance

                            }
                            else
                            {
                                bool isInitted = (bool)initializedProp.GetValue(instance, null);
                                if (isInitted)
                                {
                                    if ((uint)_zoneSupported.GetValue(instance, null) > 0)
                                    {
                                        Log($"Successfully added instance {control.Name} gpu index {gpuIdx} isslibridge: {isSliBridge}");
                                        _instances.Add(instance);
                                    } else
                                    {
                                        Log($"Instance has no supported zones, skipping {control.Name} gpu index {gpuIdx} isslibridge: {isSliBridge}");
                                    }
                                } else
                                {
                                    Log($"Instance not initted, skipping {control.Name} gpu index {gpuIdx} isslibridge: {isSliBridge}");
                                }
                            }
                        }
                    };
                    for (int gpuIdx = 0; gpuIdx < NUM_GPUS; gpuIdx++)
                    {
                        tryAddControlInstances(gpuIdx, false);
                        if (SLI_BRIDGE)
                        {
                            tryAddControlInstances(gpuIdx, true);
                        }
                    }
                }
                finally
                {
                    Directory.SetCurrentDirectory(curDir);
                    AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
                }
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                throw;
            }
        }

        public int GetNumberOfDevices()
        {
            return _instances.Count();
        }

        public int GetLedCount(int deviceId)
        {
            if (deviceId < 0 || deviceId >= _instances.Count())
            {
                return -1;
            }
            var zones = (uint)_zoneSupported.GetValue(_instances[deviceId]);
            int count = 0;
            for (int i= 0; i< 32; i++)
            {
                if (((zones>>i) & 1) == 1)
                {
                    count++;
                }
            }
            return count;
        }

        public void SetColor(int deviceId, int ledId, byte a, byte r, byte g, byte b)
        {
            try
            {
                if (deviceId < 0 || deviceId >= _instances.Count())
                {
                    return;
                }
                if (ledId < 0 || ledId > 31)
                {
                    return;
                }
                var instance = _instances[deviceId];
                _zoneSet.SetValue(instance, (uint)1 << ledId, null);
                _setLEDStaticOn.Invoke(instance, new object[] { new System.Windows.Media.Color() { A = a, R = r, B = b, G = g } });
            }
            catch { }
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = args.Name.Split(',')[0];
            var assName = Path.Combine(_evgaPath, name + ".dll");
            if (!File.Exists(assName))
            {
                assName = Path.Combine(_evgaPath, name + ".exe");
            }
            if (File.Exists(assName))
            {
                try
                {
                    var a = Assembly.Load(File.ReadAllBytes(assName));
                    return a;
                }
                catch (Exception ex)
                {
                }
            }
            return LoadFromEvgaResource(name);
        }

        private Assembly evgaAssembly;
        private List<string> evgaResources = null;
        private Assembly LoadFromEvgaResource(string name)
        {
            try
            {
                if (evgaAssembly == null)
                {
                    evgaAssembly = Assembly.ReflectionOnlyLoadFrom(_precisionExe);
                }
                if (evgaResources == null)
                {
                    evgaResources = new List<string>();
                    evgaResources.AddRange(evgaAssembly.GetManifestResourceNames());
                }
                var found = evgaResources.FirstOrDefault(x => x.ToLower().EndsWith(name.ToLower() + ".dll"));
                if (found == null)
                {
                    return null;
                }
                using (var mrs = evgaAssembly.GetManifestResourceStream(found))
                {
                    using (var ms = new MemoryStream())
                    {
                        mrs.CopyTo(ms);
                        return Assembly.Load(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                return null;
            }
        }

    }
}
