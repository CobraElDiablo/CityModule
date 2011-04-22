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

/// <summary>
/// Summary description for Class1
/// </summary>
namespace Aurora.Modules.City
{
    /// <summary>
    /// Describes a base layer for a city map, contains:
    ///     cityRegion      A link that denotes which region (scene) is being used for this map in the server instance.
    ///     age             The age of the city (simulated) in years, future to allow for the city to change dynamically with age.
    ///     multiRegion     A flag used to represent whether or not the city is in a single region (scene) or spread over multiple regions.
    ///     cityBuildings   A list of buildings that are within the city limits.
    ///     cityPlots       A list of plots used in the city map, note that a plot can contain other things apart from buildings.
    ///     cityClaims      An array of values indicating the type of claim on the land, part of a complex, transport etc.
    /// </summary>
    public class CityMap
    {
        /*
         *  This class is about to be changed, moved to it's own file and expanded in order to deal
         *  with cities covering more than one region (regardless of region size).
         */
        #region Private Members

        public List<Scene> centralRegions = new List<Scene>();          // This property can be used to store a list
        // of 'centers', for larger cities, that form the more densely packed regions, difference
        // between the center of London for example and the 'suburbs'. The are considered 'hotzones'
        // where the majority of the tallest structures will be placed.
        public Scene[,] cityRegions;                //  An array of regions covered by this city.
        public List<BuildingPlot> cityPlots = new List<BuildingPlot>();        //  A list of all the plots in this city.
        public List<CityBuilding> cityBuildings = new List<CityBuilding>();    //  A list of all buildings in this city.

        #endregion

        #region Public Members

        public int GetCentralRegionCount
        {
            get { return centralRegions.Count(); }
        }

        public int GetTotalRegions
        {
            get { return cityRegions.GetUpperBound(0) * cityRegions.GetUpperBound(1); }
        }

        public int GetNumPlots
        {
            get { return cityPlots.Count(); }
        }

        public int GetNumBuildings
        {
            get { return cityBuildings.Count(); }
        }

        public bool isPlotClaimed(BuildingPlot plot)
        {
            //  For each entry in the city plots list determine if the plot
            // given as a parameter is allocated to another building, the road
            // network etc.
            foreach (BuildingPlot p in cityPlots)
            {
                if (plot.xpos >= p.xpos && plot.ypos >= p.ypos &&
                    (plot.xpos + plot.width <= p.xpos + p.width) &&
                    (plot.ypos + plot.depth <= p.ypos + p.depth))
                {
                    //  Plot specified is in this plots area. Determine if it has been
                    // claimed for anything other than part of a complex.
                    if (p.plotFlags == PlotClaimType.CLAIM_NONE)
                        return (false);
                }
            }
            //  Defaults to returning true to indicate whether regardless of whether the
            // plot is actually claimed or not, safety fall through.
            return (true);
        }

        public bool AddScene(Scene scene, bool central)
        {
            if (scene.Equals(null))
                return (false);

            // do it the hard way and find the first available space.
            if (cityRegions.Equals(null))
                return (false);
            if (cityRegions.GetUpperBound(0) == 0 || cityRegions.GetUpperBound(1) == 0)
            {
                CityModule.m_log.Info("[CITY BUILDER]: No space in city regions!");
                return (false);
            }
            for (int rx = 0; rx < cityRegions.GetUpperBound(0); rx++)
            {
                for (int ry = 0; ry < cityRegions.GetUpperBound(1); ry++)
                {
                    if (cityRegions[rx, ry].Equals(scene))
                        return (true);
                    if (cityRegions[rx, ry].Equals(null))
                    {
                        cityRegions[rx, ry] = scene;
                        if (central)
                            centralRegions.Add(scene);
                        CityModule.m_log.InfoFormat("[CITY BUILDER]: Added new region {0} @ {1},{2}",
                            scene.RegionInfo.RegionName, rx, ry);
                        return (true);
                    }
                }
            }

            return (false);
        }

        public bool RemoveScene(Scene scene)
        {
            //  Remove the given scene from the city map
            if (scene.Equals(null))
                return (false);

            //  Firstly check to see if the region is part of one of the central regions.
            for (int c = 0; c < centralRegions.Count(); c++)
            {
                Scene s = centralRegions[c];
                if (s.Equals(scene))
                {
                    centralRegions.Remove(s);
                    break;
                }
            }

            //  Now scan the entire map and remove it.
            for (int rx = 0; rx < cityRegions.GetUpperBound(0); rx++)
            {
                for (int ry = 0; rx < cityRegions.GetUpperBound(1); ry++)
                {
                    Scene r = cityRegions[rx, ry];
                    if (r.Equals(scene))
                    {
                        cityRegions[rx, ry] = null;
                        return (true);
                    }
                }
            }

            return (false);
        }

        #endregion

        #region Constructors
        public CityMap()
        {
            cityRegions = new Scene[1, 1];
        }

        /*
        CityMap(Scene scene)
        {
            //  Construct this instance from the given scene, does not change any of the
            // plots or buildings if they are defined.
            if (scene != null)
            {
                int rxo = scene.RegionInfo.RegionLocX / cityRegions.GetUpperBound(0);
                int ryo = scene.RegionInfo.RegionLocY / cityRegions.GetUpperBound(1);

                //  Is the region rxo,ryo occupied?
                if (cityRegions[rxo, ryo].Equals(null))
                {
                    cityRegions[rxo, ryo] = scene;
                }
                else
                {
                    //  Find the first available region within the map.
                    for (int rx = 0; rx < cityRegions.GetUpperBound(0); rx++)
                    {
                        for (int ry = 0; ry < cityRegions.GetUpperBound(1); ry++)
                        {
                            if (cityRegions[rx, ry].Equals(null))
                            {
                                cityRegions[rx, ry] = scene;
                                break;
                            }
                        }
                    }
                }

                //  Now add this scene as a central scene, the reason being is that if
                // we are being constructed from a given scene then the map must not
                // exist so therefore the first scene will always be the central region
                // for a city regardless of how big the whole city is.
                centralRegions.Add(scene);
            }
        }
        */

        #endregion
    };
}
