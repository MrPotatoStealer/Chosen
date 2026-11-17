using Chosen.Core;
using Godot;

namespace Chosen.Render;

/// <summary>
/// The brightness setting (cvar brightness, 0.5-2, 1 as Blood draws it): a gamma curve over the whole picture, the
/// world, the HUD and the menus (it sits above them, under the console), like Blood's gamma levels.
/// </summary>
public partial class Brightness : CanvasLayer
{
    public static readonly Cvar Level = Cvars.Register("brightness", "0.95", "picture brightness (gamma): 0.5 darker .. 2 brighter, 1 as Blood", saved: true);
    static readonly StringName GammaName = "gamma";

    ColorRect _rect;
    ShaderMaterial _mat;

    public override void _Ready()
    {
        Layer = 90;
        _mat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = @"
shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_nearest;
uniform float gamma = 1.0;
void fragment() {
	vec3 c = texture(screen, SCREEN_UV).rgb;
	COLOR = vec4(pow(c, vec3(1.0 / gamma)), 1.0);
}
",
            },
        };
        _rect = new ColorRect { Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_rect);
    }

    public override void _Process(double delta)
    {
        float g = Mathf.Clamp(Level.Float, 0.5f, 2f);
        _rect.Visible = Mathf.Abs(g - 1) > 0.001f;
        if (!_rect.Visible)
            return;
        _rect.Position = Vector2.Zero;
        _rect.Size = GetViewport().GetVisibleRect().Size;
        _mat.SetShaderParameter(GammaName, g);
    }
}
