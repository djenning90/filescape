// Places (PLACE-02/14): the folder's landmark. Dark glass with slowly rising light bands in the accent colour.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform vec3 accent : source_color = vec3(0.3, 0.8, 1.0);
uniform float band_glow = 2.2;
// Vista spires are a MultiMesh: their accent comes from the instance colour instead.
uniform bool use_instance_color = false;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float y = world_pos.y;
	float fw = max(fwidth(y), 1e-4);
	// Thin rising rings of light on a dark glass body.
	float d = abs(fract(y * 0.4 - TIME * 0.1) - 0.5);
	float bands = 1.0 - smoothstep(0.035, 0.035 + fw * 0.6, 0.5 - d);
	float top = smoothstep(0.0, 1.0, y * 0.08);
	ALBEDO = vec3(0.03, 0.035, 0.05);
	ROUGHNESS = 0.18;
	METALLIC = 0.8;
	vec3 a = use_instance_color ? COLOR.rgb : accent;
	float rim = pow(clamp(1.0 - abs(dot(NORMAL, VIEW)), 0.0, 1.0), 3.0);
	EMISSION = a * (bands * band_glow * (0.4 + 0.6 * top) + rim * 0.5 + 0.03);
}
