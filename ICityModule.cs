using OpenMetaverse;
using OpenSim.Region.Framework.Scenes;

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
    }

}
