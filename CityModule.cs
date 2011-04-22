/*
 *
 *  City Builder Module for Aurora/OpenSim
 *       
 *  $filename   ::  CityModule.cs
 *  $author     ::  Cobra El Diablo
 *  
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Net;
using log4net;
using Nini;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Aurora.Services.DataService;
using Aurora.Simulation.Base;
using OpenSim.Data;
using OpenSim.Region;
using OpenSim.Region.CoreModules;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.CoreApplicationPlugins;
using Aurora.Framework;
using OpenSim.Framework;
using OpenSim;

namespace Aurora.Modules.City
{

    public enum GenerationStage : int
    {
        GENSTAGE_PRESTAGE = -1,
        GENSTAGE_INITIALISING,
        GENSTAGE_TERRAIN,
        GENSTAGE_CENTERS,
        GENSTAGE_DENSITY,
        GENSTAGE_FREEWAYS,
        GENSTAGE_HIGHWAYS,
        GENSTAGE_STREETS,
        GENSTAGE_RESIDENTIAL_DENSITY,
        GENSTAGE_COMMERCIAL_DENSITY,
        GENSTAGE_CORPORATE_DENSITY,
        GENSTAGE_INDUSTRIAL_DENISTY,
        GENSTAGE_BLOCKS,
        GENSTAGE_ALLOTMENT_PLOTS,
        GENSTAGE_BUILDINGS,
        GENSTAGE_COUNT
    };

    /// <summary>
    /// This is the main class that deals with this City Builder Module for Aurora/OpenSim server.
    /// </summary>
    public class CityModule : IApplicationPlugin, ICityModule//, ISharedRegionModule
    {
        /// <summary>
        /// This section of the module deals with the properties that are specific to the city or to the
        /// module itself. Some of the parameters are changeable via the set/get city commands on the console.
        /// </summary>
        #region Internal Members

        private static int startPort = 9500;
        private bool m_fEnabled = false;
        private bool m_fInitialised = false;
        private int citySeed = 0;
        private string cityName = string.Empty;
        private string cityOwner = string.Empty;
        public static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private CityMap cityMap = null;
        private IConfig cityConfig = null;
        private IConfigSource configSource = null;
        public SceneGraph sceneGraph = null;
        public SceneManager sceneManager = null;
        private ISimulationBase simulationBase = null;
        private IRegionInfoConnector m_connector = null;

        private List<float> cityDensities = new List<float>();
        #endregion
        /// <summary>
        /// Internal methods private to the City Module.
        /// </summary>
        /// <param name="range"></param>
        /// <returns></returns>
        #region Internal Methods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="range"></param>
        /// <returns></returns>
        private void InstallModule()
        {
            //
            //  CONSOLE COMMAND INTERFACE
            //
            //      Adds various commands to the server's console to allow for direct manipulation
            // of the module and it's internal properties.
            //
            //  Add a command city set to allow for the direct setting of the properties of the module.
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city set", "Sets an internal property to the given value.",
                "Allows for the manipulation of the internal property values in the City Builder Module",
                cmdSetParameter);

            //  Add a command city get to allow for the display of the current value for a module property.
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city generate", "Auto generate a city from given parameters.",
                "This command will generate a city across an entire region or server instance of regions " +
                "based on the given parameters", cmdGenerate);

            //export
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city export",
                "Export the current settings for this city to a file.",
                "Exports current module settings for the city for each region that is part of the city",
                cmdExport);

            //import
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city import",
                "Imports a previously saved city definition file, or import from GTA IV PC",
                "Imports the settings required for a given city and recreate it inworld on one or more regions",
                cmdImport);

            //help
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city help",
                "Display help information",
                "Display some help information about the use of the City Builder module from the command console",
                cmdHelp);

            //reset
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city reset",
                "Reset the internal properties of the module to the ones used during development.",
                "Reset properties to 'Cobra ElDiablo' (City Owner), 'Liberty City' (City Name), Lave (City Region [SINGLE])",
                cmdReset);

            // enable
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city enable",
                "Enables the City Module.", "Allows for the module to be re-enabled after changing internal properties",
                cmdEnable);

            // disable
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand("city", true, "city disable",
                "Disables the City Module.", "Allows for the module to be disabled before altering internal properties.",
                cmdDisable);

            //  add a command 'city info' to the main console.
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city info", "Displays information from the City Builder Module.",
                "Displays information about the current parameters from the city, like the number of buildings.",
                cmdInfo);

            //  add a 'city building' command (note has subcommands)
            OpenSim.Framework.MainConsole.Instance.Commands.AddCommand(
                "city", true, "city building", "City building interface",
                "Allow for the manipulation of buildings directly.",
                cmdBuilding);
            cityDensities.Add(0.85f);
            cityDensities.Add(0.65f);
            cityDensities.Add(0.60f);
            cityDensities.Add(0.56f);

            cityMap = new CityMap();
            if (cityMap.cityRegions.GetUpperBound(0) <= 0 || cityMap.cityRegions.GetUpperBound(1) <= 0)
            {
                cityMap.cityRegions = new Scene[1,1];
            }
            m_fInitialised = true;
        }

        public static int randomValue(int range)
        {
            int r = 0;
            Random rnd = new Random();
            r = rnd.Next(range);
            return r;
        }

        /// <summary>
        /// This method will lay claim to a plot of land in the city map.
        /// </summary>
        /// <param name="x">The desired x position of the sw corner of the plot.</param>
        /// <param name="y">The desired y position of the sw corner of the plot.</param>
        /// <param name="width">The desired width of the plot, x+width = x position for ne corner.</param>
        /// <param name="depth">The desired depth of the plot, y+depth = y position for ne corner.</param>
        /// <param name="val">The type of plot this is, ie does it have a building, road, etc on it.</param>
        private void claim(int x, int y, int width, int depth, PlotClaimType val)
        {
            if (claimed(x, y, width, depth))
                return;
            cityMap.cityPlots.Add(makePlot(x, y, width, depth));
        }

        /// <summary>
        /// Determine whether a defined area of land has been claimed by a plot already, it is
        /// not concerned as to what type of plot it is just whether it is claimed or not.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="width"></param>
        /// <param name="depth"></param>
        /// <returns></returns>
        private bool claimed(int x, int y, int width, int depth)
        {
            return (cityMap.isPlotClaimed(makePlot(x, y, width, depth)));
        }
        /// <summary>
        /// Finds which building plot is occupying the point of land specified.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private BuildingPlot findPlot(int x, int y)
        {
            foreach (BuildingPlot p in cityMap.cityPlots)
            {
                if (x >= p.xpos && y >= p.ypos)
                {
                    return (p);
                }
            }
            return null;
        }
        /// <summary>
        /// Construct a new plot from given parameters.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="z"></param>
        /// <param name="w"></param>
        /// <param name="h"></param>
        /// <returns></returns>
        private BuildingPlot makePlot(int x, int z, int w, int h)
        {
            BuildingPlot plot = new BuildingPlot();
            plot.xpos = x; plot.ypos = z;
            plot.width = (byte)w; plot.depth = (byte)h;

            return plot;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="GTAIV"></param>
        /// <returns></returns>
        private bool doExport(string filePath, bool GTAIV)
        {
            if (GTAIV) return (false);
            return (false);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="GTAIV"></param>
        /// <returns></returns>
        private bool doImport(string filePath, bool GTAIV)
        {
            //  Import the file from the specified file path. For now the GTAIV flag is
            // ignored.

            m_log.InfoFormat("[CITY BUILDER]: Importing from {0}", filePath);
            return (false);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="x1"></param>
        /// <param name="z1"></param>
        /// <param name="direction"></param>
        private void doLightStrip(int x1, int z1, int direction)
        {
            ;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="x1"></param>
        /// <param name="?"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void doRoad(int x1, int y1, int width, int depth)
        {
            int lanes;
            int divider;
            int sidewalk;
            if (width > depth)
                lanes = depth;
            else
                lanes = width;
            if (lanes < 4)
                return;
            bool odd = false;
            int i = (lanes % 2);
            if (i > 0) odd = true;
            if (odd)//lanes % 2)
            {
                lanes--;
                divider = 1;
            }
            else
                divider = 0;
            sidewalk = 2;// MAX(2, (lanes - 10));
            lanes -= sidewalk;
            sidewalk /= 2;
            lanes /= 2;
            claim(x1, y1, width, depth, PlotClaimType.CLAIM_NONE);
            if (width > depth)
            {
                claim(x1, y1 + sidewalk, width, lanes, PlotClaimType.CLAIM_TRANSPORT);
                claim(x1, y1 + sidewalk + lanes + divider, width, lanes, PlotClaimType.CLAIM_TRANSPORT);
            }
            else
            {
                claim(x1 + sidewalk, y1, lanes, depth, PlotClaimType.CLAIM_TRANSPORT);
                claim(x1 + sidewalk + lanes + divider, y1, lanes, depth, PlotClaimType.CLAIM_TRANSPORT);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="plot"></param>
        private void doBuilding()
        {
            //  Construct a random building and place it into the buildings list.
            CityBuilding building;

            BuildingType type = BuildingType.BUILDING_CIVIL | BuildingType.BUILDING_GENERAL;
            BuildingFlags flags = BuildingFlags.BUILDING_FLAG_ACOND | BuildingFlags.BUILDING_FLAG_LIGHTS |
                BuildingFlags.BUILDING_FLAG_LOGO | BuildingFlags.BUILDING_FLAG_TRIM;
            BuildingPlot plot = new BuildingPlot();
            plot.xpos = randomValue(256) / 4;
            plot.ypos = randomValue(256) / 4;
            plot.width = (byte)randomValue(10);
            plot.depth = (byte)randomValue(10);
            plot.plotFlags = PlotClaimType.CLAIM_BUILDING | PlotClaimType.CLAIM_COMPLEX;

            building = new CityBuilding(type, plot, flags, UUID.Zero,cityMap.centralRegions[0],"Building");
        }

        public bool createRegion(int x, int y)
        {
            RegionInfo region = new RegionInfo();

            region.RegionName = "Region"+x+y;
            region.RegionID = UUID.Random();
            region.RegionLocX = 100 + x;// * Constants.RegionSize);
            region.RegionLocY = 100 + y;// * Constants.RegionSize);
            
            IPAddress address = IPAddress.Parse("0.0.0.0");
            region.InternalEndPoint = new IPEndPoint(address, startPort++);

            region.ExternalHostName = Aurora.Framework.Utilities.GetExternalIp();
            region.FindExternalAutomatically = true;

            region.RegionType = "mainland";
            region.ObjectCapacity = 80000;//int.Parse(ObjectCount.Text);

            region.RegionSettings.Maturity = 0;
            region.Disabled = false;//DisabledEdit.Checked;
            region.RegionSizeX = 256;//int.Parse(CRegionSizeX.Text);
            region.RegionSizeY = 256;//int.Parse(CRegionSizeY.Text);
            if ((region.RegionSizeX % Constants.MinRegionSize) != 0 ||
                (region.RegionSizeY % Constants.MinRegionSize) != 0)
            {
                return(false);
            }
            region.NumberStartup = 0+(x*y)+y;
            region.Startup = StartupType.Normal;

            m_log.Info("[CITY BUILDER]: Creating Region: (" + region.RegionName + ")");

            // set the initial ports
            region.HttpPort = MainServer.Instance.Port;

            AgentCircuitManager circuitManager = new AgentCircuitManager();
            IPAddress listenIP = region.InternalEndPoint.Address;
            Scene scene = new Scene();

            if (!IPAddress.TryParse(region.InternalEndPoint.Address.ToString(), out listenIP))
                listenIP = IPAddress.Parse("0.0.0.0");

            uint port = (uint)region.InternalEndPoint.Port;

            string ClientstackDll = configSource.Configs["Startup"].GetString("ClientStackPlugin", "OpenSim.Region.ClientStack.LindenUDP.dll");

            IClientNetworkServer clientServer = AuroraModuleLoader.LoadPlugin<IClientNetworkServer>(ClientstackDll);
            clientServer.Initialise(
                    listenIP, ref port, 0, region.m_allow_alternate_ports,
                    configSource, circuitManager);

            region.InternalEndPoint.Port = (int)port;

            scene.AddModuleInterfaces(simulationBase.ApplicationRegistry.GetInterfaces());
            scene.Initialize(region, circuitManager, clientServer);

//            StartModules(scene);

//            m_clientServers.Add(clientServer);

            //Do this here so that we don't have issues later when startup complete messages start coming in
//            m_localScenes.Add(scene);

            m_log.Info("[Modules]: Loading region modules");
            IRegionModulesController controller;
            if (simulationBase.ApplicationRegistry.TryRequestModuleInterface(out controller))
            {
                controller.AddRegionToModules(scene);
            }
            else
                m_log.Error("[Modules]: The new RegionModulesController is missing...");

            //Post init the modules now
//            PostInitModules(scene);

            //Start the heartbeats
            scene.StartHeartbeat();
            //Tell the scene that the startup is complete 
            // Note: this event is added in the scene constructor
            scene.FinishedStartup("Startup", new List<string>());
           
            /*
            try
            {
                m_connector.UpdateRegionInfo(region);
                sceneManager.CreateRegion(region, out scene);
                scene.RegisterModuleInterface<ICityModule>(this);
            }
            catch (System.Exception e)
            {
                m_log.Info("Exception caught when trying to create a region from the scene manager.");
                return (false);
            }
            */
            return (true);
        }
        /// <summary>
        /// This method will produce a random city with the central region of the city being
        /// specified as a parameter. More parameters need to be made available for this method
        /// to produce a better quality city, note for now the minimum area for a city is a
        /// 3x3 grid of regions. This code is based on the original C++ version called pixel city.
        /// </summary>
        /// <param name="seed_value">Random integer seed value.</param>
        /// <returns>true / false indicator of success or failure.</returns>
        private bool doGenerate(int seed_value)
        {
            //  Based on the initial seed value populate the regions that this shared module 
            // is connected to, this means first get a list of the region, determine which
            // region is in the center of all the regions and set this as the hotzone, or
            // central part of the city (this is where the tallest/largest buildings will 
            // be created) and will extend out to cover virtually all of the connected
            // regions if desired. No support for aging of the buildings or the city exists
            // yet it is a possible course for the future of this module.

            //  First quick check to see if the module is enabled or not.
            if (!m_fEnabled)
            {
                m_log.Info("[CITY BUILDER]: Disabled, aborting auto generation.");
                return (false);
            }

            m_log.Info("[CITY BUILDER]: Auto generating the city.");

            //  Now we need to ask some basic values for the city generation, we already have
            // the base seed value as this is part of the 'city generate' command, now what
            // about a name, position, size, densities etc. Some of this can be generated
            // based on the seed value, but then, it would need to be confirmed by the user
            // or allow them to change it.

            //  Decide where the city is to be placed within the server instance.
            int r = CityModule.randomValue(16);// (int)(27 / 2.45f + (((4 / 5) * 4) / 3));

            string regionCount = MainConsole.Instance.CmdPrompt("Region Count ", r.ToString());
            r = Convert.ToInt32(regionCount);
            m_log.InfoFormat("[CITY BUILDER]: Region size 256, x:{0} * y:{1} {2}, regions.", r, r, r * r);
            m_log.InfoFormat("[CITY BUILDER]: City area {0} m^2", (256 * r) * (256 * r));

            cityName = MainConsole.Instance.CmdPrompt("City Name ", cityName);
            cityOwner = MainConsole.Instance.CmdPrompt("City Owner ", cityOwner);

            //  Obtain the scene manager, scene graph and region info connector from the server.
            if (simulationBase.Equals(null))
            {
                m_log.Info("[CITYBUILDER]: Unable to continue, no simulation base!");
                return (false);
            }
            /*
             * Change following to:
             *      m_connector = Aurora.DataManager.DataManager.RequestPlugin<IRegionInfoConnector>();
             */
            sceneManager = simulationBase.ApplicationRegistry.RequestModuleInterface<SceneManager>();
            sceneGraph = simulationBase.ApplicationRegistry.RequestModuleInterface<SceneGraph>();
            m_connector = simulationBase.ApplicationRegistry.RequestModuleInterface<IRegionInfoConnector>();

            if (sceneManager.Equals(null))
            {
                m_log.Error("NO SCENE MANAGER FOUND.");
                return (false);
            }
            //  Construct the data instance for a city map to hold the total regions in the simulation.
            cityMap = new CityMap();
            citySeed = seed_value;
            cityMap.cityRegions = new Scene[r, r];

            m_log.InfoFormat("[CITY BUILDER]: r {0}", r);

            for (int rx = 0; rx < r; rx++)
            {
                for (int ry = 0; ry < r; ry++)
                {
                    if (!createRegion(rx, ry))
                    {
                        m_log.InfoFormat("[CITY BUILDER]: Failed to construct region at {0},{1}", rx, ry);
                        return (false);
                    }
                }
            }

            /*
             *  According to the region plugin loader the following is the correct way of constructing
             *  regions, although in this case it is causing an exception to be thrown.
             *  
             *      Does a region being created have to have a region file for it or can the region information
             *      be generated procedurally?
             *      
             *      How can I create, delete and manipulate primitives without relying on an inworld script
             *      with an XMLRPC/HTTP, etc communications method as the maximum data size allowed is too small.
            uint port = 0;
            //  Here we construct a 10 x 10 region space for the city.
            for (int rx = 0; rx < 10; rx++)
            {
                for ( int ry = 0; ry < 10; ry++)
                {
                    OpenSim.Framework.RegionInfo regionInfo = new OpenSim.Framework.RegionInfo();
                    CityMap map = new CityMap();

                    regionInfo.RegionLocX = (100 + rx);
                    regionInfo.RegionLocY = (100 + ry);
                    regionInfo.RegionID = new UUID();
                    regionInfo.RegionName = Convert.ToString("TestRegion" + rx + ry);
                    regionInfo.RegionSizeX = 1024;
                    regionInfo.RegionSizeY = 1024;
                    regionInfo.HttpPort = port;
                    regionInfo.RegionType = "Mainland";
                    regionInfo.Password = UUID.Parse("9ac04bbd-cecb-4548-ae4d-ebe5c013985a");
                    regionInfo.m_allow_alternate_ports = false;
                    port++;

                    IConfigSource configSource = new Nini.Config.IConfigSource();
                    regionInfo.CreateIConfig(configSource);
                    if (configSource != null)
                    {
                        configSource.AutoSave = true;
                        configSource.Save();
                    }

                    map.age = 0;
                    map.multiRegion = true;
                    map.cityPlots = new List<BuildingPlot>();
                    map.cityBuildings = new List<CityBuilding>();

                    try
                    {
                        IScene scene;
                        sceneManager.CreateRegion( regionInfo, out scene );
                        map.sceneIF = scene;
                        map.cityRegion.RegisterModuleInterface<ISharedRegionModule>(this);
                        cityMap.Add(map);
                        m_log.InfoFormat("[CITY BUILDER]: Region {0} created.", regionInfo.RegionName);
                    }
                    catch ( System.Exception E )
                    {
                        m_log.Info("[CITY BUILDER]: Exception caught when trying to create a region.");
                        return( false );
                    }
                }
            }
            */
            m_log.Info("[CITY BUILDER]: [TERRAIN]");
            m_log.Info("[CITY BUILDER]: [CENTERS]");
            m_log.Info("[CITY BUILDER]: [DENSITY]");
            m_log.Info("[CITY BUILDER]: [FREEWAYS]");
            m_log.Info("[CITY BUILDER]: [HIGHWAYS]");
            m_log.Info("[CITY BUILDER]: [STREETS]");
            m_log.InfoFormat("[CITY BUILDER]: [RESIDENTIAL DENSITY] {0}%", cityDensities[0] * 100);
            m_log.InfoFormat("[CITY BUILDER]: [COMMERCIAL DENSITY] {0}%", cityDensities[1] * 100);
            m_log.InfoFormat("[CITY BUILDER]: [CORPORATE DENSITY] {0}%", cityDensities[2] * 100);
            m_log.InfoFormat("[CITY BUILDER]: [INDUSTRIAL DENISTY] {0}%", cityDensities[3] * 100);
            m_log.Info("[CITY BUILDER]: [BLOCKS]");
            m_log.Info("[CITY BUILDER]: [ALLOTMENT PLOTS]");
            m_log.Info("[CITY BUILDER]: [BUILDINGS]");

            return (true);
        }

        #endregion
        #region ICityModule Methods

        public SceneManager SceneManager
        {
            get { return sceneManager; }
        }

        public SceneGraph SceneGraph
        {
            get { return sceneGraph; }
        }

        #endregion
        /// <summary>
        /// This section deals with the inherited interface IApplicationPlugin, note some methods
        /// are duplicates of ones in the ISharedRegionModule interface region.
        /// </summary>
        #region IApplicationPlugin Region
        /// <summary>
        /// This method is during the startup of the Aurora server, It is called just after the
        /// HTTP server has started and before any shared modules or regions have been loaded.
        /// </summary>
        /// <param name="config">Configuration parameter stream.</param>
        public void Initialize(ISimulationBase openSim)
        {
            //  Display a startup message, save the passed 'server instance?', obtain the
            // scene manager and scene graph for this server instance and obtain the interface
            // used to access region information, regardless of whether the storage method is
            // file, web or MySQL based in reality. Finally call an internal method that installs
            // the command console commands and alters some internal properties, indicating that
            // the module is loaded, enabled and ready for use.
            m_log.Info("[CITY BUILDER]: Version 0.0.0.9 ");
            simulationBase = openSim;
//            sceneManager = simulationBase.ApplicationRegistry.RequestModuleInterface<SceneManager>();
//            sceneGraph = simulationBase.ApplicationRegistry.RequestModuleInterface<SceneGraph>();
//            m_connector = simulationBase.ApplicationRegistry.RequestModuleInterface<IRegionInfoConnector>();
            InstallModule();

        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="config"></param>
        public void ReloadConfiguration(IConfigSource config)
        {
            m_log.Info("CityModule.ReloadConfiguration");
        }

        public void Start()
        {
            m_log.Info("CityModule.Start");
        }

        public void PostStart()
        {
            m_log.Info("CityModule.PostStart");
        }

        public void Close()
        {
            m_log.Info("[CITY BUILDER]: Terminating.");
            m_fEnabled = false;
        }

        /// <summary>
        /// This is called when the module has been loaded.
        /// </summary>
        public void PostInitialise()
        {
            m_log.Info("[CITY BUILDER] finished initialising.");
        }

        /// <summary>
        /// Returns the name of the module, "City Builder Module".
        /// </summary>
        public string Name
        {
            get { return "CityBuilder"; }
        }

        public void Dispose()
        {
            m_log.Info("CityModule.Dispose");
        }

        #endregion
        /*
        /// <summary>
        /// ISharedRegionModule interface specifics methods, some are shared with IApplicationPlugin
        /// interface methods. REMOVED.
        /// </summary>
        /// <param name="range"></param>
        /// <returns></returns>
        #region ISharedRegionModule interface

        public void Initialise(IConfigSource config)
        {
            configSource = config;
            
            cityConfig = config.Configs["CityBuilder"];
            if (cityConfig != null)
            {
                cityName = cityConfig.GetString("Name", "01");
                cityOwner = cityConfig.GetString("CityOwner", "Cobra ElDiablo");
                citySeed = randomValue(65535);
                if (cityName.Length > 0 && cityOwner.Length > 0)
                    m_fEnabled = true;
                else
                    m_fEnabled = false;
            }
            else
            {
                m_log.Warn("Configuration not found.");
                m_fEnabled = false;
            }
            if (m_fInitialised)
                return;
            InstallModule();
        }

        /// <summary>
        /// This method is called for every scene, or region, that is present on the
        /// single instance of the server. Note this is standalone tested only at present.
        /// </summary>
        /// <param name="scene">The scene, or region being added to the server instance and thus this module.</param>
        public void AddRegion(Scene scene)
        {
            //  If the module is not enabled do not add anything.
            if (!m_fEnabled || scene.Equals(null))
                return;

//            bool flag = false;

            //  Now this is a single module instance that deals with all regions within
            // the server instance. We need to register ourselves with ALL regions that
            // are being added to the server.
            m_log.InfoFormat("[CITY BUILDER]: Adding region {0}.", scene.RegionInfo.RegionName);
            scene.RegisterModuleInterface<ISharedRegionModule>(this);
            cityMap.AddScene(scene, true);
//            if (
//                !cityMap.Equals(null) && !scene.Equals(null) && 
//                cityMap.cityRegions.GetLength(0)>0 && cityMap.cityRegions.GetLength(1)>0
//                )
//            {
//                cityMap.cityRegions[scene.RegionInfo.RegionLocX % cityMap.cityRegions.GetUpperBound(0),
//                    scene.RegionInfo.RegionLocY % cityMap.cityRegions.GetUpperBound(1)] = scene;
//                if (cityMap.centralRegions.Count() <= 0)
//                {
//                    cityMap.centralRegions.Add(scene);
//                }
//                flag = true;
//            }
//            if (flag)
//            {
//                m_log.Info("[CITY BUILDER]: addition successful.");
//            }
//            else
//            {
//                m_log.Info("[CITY BUILDER]: failed.");
//            }
        }

        /// <summary>
        /// This is called when each scene or region is being removed from the single instance
        /// of the server.
        /// </summary>
        /// <param name="scene"></param>
        public void RemoveRegion(Scene scene)
        {
            scene.UnregisterModuleInterface<ISharedRegionModule>(this);
            cityMap.RemoveScene(scene);
            m_log.InfoFormat("[CITY BUILDER] Removed region {0}", scene.RegionInfo.RegionName);
        }

        /// <summary>
        /// This is called when each region has been loaded.
        /// </summary>
        /// <param name="scene"></param>
        public void RegionLoaded(Scene scene)
        {
            m_log.InfoFormat("[CITY BUILDER] Region {0} loaded", scene.RegionInfo.RegionName);
        }

        /// <summary>
        /// No idea, just copied from other modules from within Aurora, no idea what this method
        /// is actually used for.
        /// </summary>
        public Type ReplaceableInterface
        {
            get { return null; }
        }

        /// <summary>
        /// Returns true/false on whether the module is to be shared by all regions or each
        /// region has it's own instance of this module.
        /// </summary>
        public bool IsSharedModule
        {
            get { return true; }
        }

        #endregion
        */
        /// <summary>
        /// Console command interface method.
        /// </summary>
        /// <param name="range"></param>
        /// <returns></returns>
        #region Command Region
        
        /// <summary>
        /// This method decodes the 'city set' console command.
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdSetParameter(string module, string[] cmdParams)
        {
            string param = string.Empty;
            string value = string.Empty;

            if (cmdParams.Length != 4)
            {
                m_log.InfoFormat("Invalid number of parameters supplied ({0})", cmdParams.Length);
                m_log.Info("city set <parameter> <value>");
                return;
            }

            param = cmdParams[2];
            value = cmdParams[3];

            m_log.InfoFormat("Attempting to set parameter {0} to {1}", param, value);

            if (param == "Name")
            {
                m_log.InfoFormat("City name changed from {0}, to {1}", cityName, value);
                cityName = value;
                return;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdGetParameter(string module, string[] cmdParams)
        {
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdExport(string module, string[] cmdParams)
        {
            m_log.InfoFormat("[CITY BUILDER] : Exporting to file {0}", cmdParams[2]);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdImport(string module, string[] cmdParams)
        {
            m_log.InfoFormat("[CITY BUILDER] : Importing from file {0}", cmdParams[2]);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdHelp(string module, string[] cmdParams)
        {
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdReset(string module, string[] cmdParams)
        {
            //  Reset the module, first disable, then reset then reenable.
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdEnable(string module, string[] cmdParams)
        {
            if (m_fEnabled)
                return;
            m_log.Info("[CITY BUILDER] : Enabling");
            m_fEnabled = true;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdDisable(string module, string[] cmdParams)
        {
            if (!m_fEnabled)
                return;
            m_log.Info("[CITY BUILDER] : Disabling.");
            m_fEnabled = false;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdGenerate(string module, string[] cmdParams)
        {
//            if (cmdParams.Length != 6 || cmdParams.Length != 7)
//            {
//                m_log.InfoFormat("[CITY BUILDER] : Bad parameters supplied to city generate. {0}",cmdParams.Length);
//                m_log.Info("[CITY BUILDER] : city generate <region> <seed> <city name> <city owner> <all/single>");
//                return;
//            }

            doGenerate(32767);
        }
        /// <summary>
        /// Handles one of the builtin commands on the main server's command console, this command
        /// is 'city info' it displays various information about the city, regions and buildings
        /// on the main servers console.
        /// </summary>
        /// <param name="module">The name of the module, should be 'city' or the actual modules name.</param>
        /// <param name="cmdparams">A list of strings making up the command including the actual command at index 0.</param>
        public void cmdInfo(string module, string[] cmdParams)
        {
            if (cmdParams[1] == "info")
            {
                m_log.InfoFormat("[CITY BUILDER]: City Name    : {0}", cityName);
                m_log.InfoFormat("[CITY BUILDER]: City Owner   : {0}", cityOwner);
                m_log.InfoFormat("[CITY BUILDER]: Region count : {0}",
                    cityMap.cityRegions.GetUpperBound(0) * cityMap.cityRegions.GetUpperBound(1));
                m_log.Info("[CITY BUILDER]: Street information");
                m_log.InfoFormat("[CITY BUILDER]: Plot count   : {0}", cityMap.cityPlots.Count());
                m_log.InfoFormat("[CITY BUILDER]: Buildings    : {0}", cityMap.cityBuildings.Count());
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdParams"></param>
        public void cmdBuilding(string module, string[] cmdParams)
        {
            m_log.Info("[CITY BUILDER] : Buildings commands.");

            string name = MainConsole.Instance.CmdPrompt("Building name ", "Building");
            string owner = MainConsole.Instance.CmdPrompt("Owner", "Cobra ElDiablo");

            doBuilding();

        }

        #endregion
        #region Public Methods
        /// <summary>
        /// Constructs a cube at a given location and size.
        /// </summary>
        /// <param name="pos" type="Vector3">The position of the cube, at it's center, [X,Y,Z]</param>
        /// <param name="dim" type="Vector3">The size of the cube.</param>
        /// <returns>A UUID for the newly created cube or UUID.Zero on failure.</returns>
        /// </summary>
        public PrimitiveBaseShape createCube(Vector3 pos, Vector3 dim, UUID TextureID)
        {
            //  Construct a cube at a given position, size and texture UUID.
            PrimitiveBaseShape cubeShape = PrimitiveBaseShape.CreateBox();// new PrimitiveBaseShape();
            cubeShape.ToOmvPrimitive(pos, Quaternion.Identity);
            cubeShape.Scale = dim;
            sceneGraph.AddNewPrim(UUID.Zero, UUID.Zero, pos, Quaternion.Identity, cubeShape);
            return cubeShape;
        }
        #endregion

    }

}
