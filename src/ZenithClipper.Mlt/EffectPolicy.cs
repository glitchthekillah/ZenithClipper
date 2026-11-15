using System.Globalization;
using ZenithClipper.Editing;
namespace ZenithClipper.Mlt;

internal static class EffectPolicy
{
    static readonly Dictionary<string,Dictionary<string,(double Min,double Max)>> schemas=new(StringComparer.Ordinal)
    {
        ["brightness"]=new(){["level"]=(0,15),["alpha"]=(0,1),["rgb_only"]=(0,1)},
        ["gamma"]=new(){["gamma"]=(.01,10)},
        ["box_blur"]=new(){["hradius"]=(0,100),["vradius"]=(0,100),["preserve_alpha"]=(0,1)},
        ["volume"]=new(){["level"]=(-120,24)}
    };
    internal static void Validate(ClipEffect effect)
    {
        if(EffectCatalog.Find(effect.Id) is {} def)
        {
            if(effect.Keyframes.Count>0)throw new ArgumentException($"{def.Name} does not support keyframes yet.");
            foreach(var pair in effect.Parameters)
            {
                var param=def.Params.FirstOrDefault(x=>x.Key==pair.Key)??throw new ArgumentException($"Unsupported parameter '{pair.Key}' for {def.Name}.");
                switch(param.Type)
                {
                    case ParamType.Number: if(!double.TryParse(pair.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<param.Min||n>param.Max)throw new ArgumentException($"{param.Label} must be {param.Min}–{param.Max}."); break;
                    case ParamType.Choice: if(param.Choices is null||!param.Choices.Contains(pair.Value))throw new ArgumentException($"Unknown choice for {param.Label}."); break;
                    case ParamType.Color: if(pair.Value.Length!=7||pair.Value[0]!='#'||!pair.Value.Skip(1).All(Uri.IsHexDigit))throw new ArgumentException($"{param.Label} must be a #RRGGBB color."); break;
                    case ParamType.File: if(pair.Value.Length>0&&!File.Exists(pair.Value)||!pair.Value.EndsWith(".cube",StringComparison.OrdinalIgnoreCase)&&!pair.Value.EndsWith(".3dl",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Pick an existing .cube or .3dl LUT file."); break;
                }
            }
            return;
        }
        if(!schemas.TryGetValue(effect.Id,out var schema))throw new ArgumentException($"Effect '{effect.Id}' is not supported by this editor build.");
        foreach(var pair in effect.Parameters)
        {
            if(!schema.TryGetValue(pair.Key,out var range))throw new ArgumentException($"Unsupported parameter '{pair.Key}' for {effect.Id}.");
            string input=pair.Value;if(effect.Id=="volume" && pair.Key=="level" && input.EndsWith("dB",StringComparison.Ordinal))input=input[..^2];
            if(!double.TryParse(input,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number)||number<range.Min||number>range.Max)throw new ArgumentException($"Invalid value for {effect.Id}/{pair.Key}.");
        }
        foreach(var key in effect.Keyframes)
            if(!schema.TryGetValue(key.Parameter,out var range)||!double.IsFinite(key.Value)||key.Value<range.Min||key.Value>range.Max||key.Frame<0||key.Frame>int.MaxValue||!ClipBlend.Interpolations.Contains(key.Interpolation))throw new ArgumentException("Unsupported effect keyframe.");
    }
}
