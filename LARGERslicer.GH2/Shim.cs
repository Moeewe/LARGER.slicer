// The slicer core unwraps Grasshopper 1 data in one place (SliceEngine.ToMesh). Grasshopper 2
// hands over plain geometry, so this empty stand-in is all the core needs to compile here.
namespace Grasshopper.Kernel.Types
{
    public interface IGH_Goo { object ScriptVariable(); }
}
