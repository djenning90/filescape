// A picture on its canvas (PLANET-132, PLANET-133; PlanetVisual.Pictures): the picture fitted inside a cream mat with its
// shape kept (never stretched), and the canvas's own state when there is no picture yet — waiting (the plain mat), loading
// (a slow sheen crossing the mat), or failed (a dimmer mat with one thin slash). Unshaded, so a picture keeps its colours
// at night, slightly under full brightness so the world's bloom never blows it out. UV (0,0) is the canvas's top left.
shader_type spatial;
render_mode unshaded, cull_back;

uniform sampler2D picture : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float picture_aspect = 1.0; // the picture's width / height
uniform float face_aspect = 1.0;    // the canvas's width / height
uniform vec3 mat_colour : source_color = vec3(1.0, 0.95, 0.86);
uniform float state = 0.0;          // 0 waiting, 1 loading, 2 shown, 3 failed
uniform float margin = 0.035;       // the mat round the picture, as a share of the canvas
uniform float brightness = 0.94;
uniform float sheen_rate = 2.4;     // the loading sheen's speed, radians per second (one pass in ≈ 2.6 s)

void fragment() {
	vec2 size = picture_aspect > face_aspect ? vec2(1.0, face_aspect / picture_aspect) : vec2(picture_aspect / face_aspect, 1.0);
	size *= 1.0 - 2.0 * margin;
	vec2 lo = (vec2(1.0) - size) * 0.5;
	vec2 p = (UV - lo) / size;
	bool inside = p.x >= 0.0 && p.y >= 0.0 && p.x <= 1.0 && p.y <= 1.0;
	vec3 c = mat_colour * 0.92;
	if (state > 1.5 && state < 2.5) {
		c = inside ? texture(picture, p).rgb * brightness : mat_colour * 0.92;
	} else if (state > 0.5 && state < 1.5) {
		float sheen = 0.5 + 0.5 * sin((UV.x + UV.y) * 5.0 - TIME * sheen_rate);
		c = mix(mat_colour * 0.78, mat_colour, sheen * sheen);
	} else if (state > 2.5) {
		c = mat_colour * 0.55;
		if (abs(UV.x - UV.y) < 0.018 && UV.x > 0.3 && UV.x < 0.7) c = vec3(0.62, 0.38, 0.33);
	}
	ALBEDO = c;
}
