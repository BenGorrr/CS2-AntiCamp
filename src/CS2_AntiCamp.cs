using Microsoft.Extensions.DependencyInjection;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared;

namespace CS2_AntiCamp;

[PluginMetadata(Id = "CS2_AntiCamp", Version = "1.0.0", Name = "CS2 Anti Camp", Author = "BenGorr", Description = "No description.")]
public partial class CS2_AntiCamp : BasePlugin {
  public CS2_AntiCamp(ISwiftlyCore core) : base(core)
  {
  }

  public override void ConfigureSharedInterface(IInterfaceManager interfaceManager) {
  }

  public override void UseSharedInterface(IInterfaceManager interfaceManager) {
  }

  public override void Load(bool hotReload) {
    
  }

  public override void Unload() {
  }
} 