using OpenMetaverse;
using OpenSim.Region.Framework.Scenes;

using Nini;
using Nini.Config;

using Aurora.Simulation.Base;

namespace Aurora.Framework
{
    /// <summary>
    /// This interface allows for access to some of the internals of City Builder.
    /// </summary>
    public interface ICityModule
    {
        SceneManager SceneManager
        {
            get;
        }
        SceneGraph SceneGraph
        {
            get;
        }
        Vector2 CitySize
        {
            get;
        }
        IConfigSource ConfigSource
        {
            get;
        }
        SimulationBase SimulationBase
        {
            get;
        }
    }
}
