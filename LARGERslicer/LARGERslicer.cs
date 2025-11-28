using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace LARGERslicer
{
  public class LARGERslicerInfo : GH_AssemblyInfo
  {
    public override string Name => "LARGERslicer";

    //Return a 24x24 pixel bitmap to represent this GHA library.
    public override Bitmap Icon => null;

    //Return a short string describing the purpose of this GHA library.
    public override string Description => "Advanced slicing operations and mesh processing tools for Grasshopper";

    public override Guid Id => new Guid("2e6c19c9-5ab9-4fb9-bfc7-b56066ef39b1");

    //Return a string identifying you or your company.
    public override string AuthorName => "Moritz Wesseler, FH Münster";

    //Return a string representing your preferred contact details.
    public override string AuthorContact => "m.wesseler@fh-muenster.de";

    //Return a string representing the version.  This returns the same version as the assembly.
    public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
  }

  public class LARGERslicerCategoryIcon : GH_AssemblyPriority
  {
    public override GH_LoadingInstruction PriorityLoad()
    {
      // Register the LARGERslicer category symbol and short name
      // This is the ONLY category registration needed - subcategories are automatically
      // created from component base() constructors
      Grasshopper.Instances.ComponentServer.AddCategorySymbolName("LARGERslicer", 'L');
      Grasshopper.Instances.ComponentServer.AddCategoryShortName("LARGERslicer", "LARGER");

      return GH_LoadingInstruction.Proceed;
    }
  }
} 