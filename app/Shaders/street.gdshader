// District streets: emissive strips with slow pulses travelling from parent to child (the "data flow").
// UV.x runs along the segment, scaled by INSTANCE_CUSTOM.x (the segment length).
shader_type spatial;
render_mode unshaded, cull_back;

uniform vec3 street_color : source_color = vec3(0.1, 0.75, 1.0);
uniform float glow = 1.6;
uniform float pulse_speed = 6.0;

varying vec3 local_pos;
varying float seg_len;
varying float seg_offset;

void vertex() {
	local_pos = VERTEX;
	seg_len = max(INSTANCE_CUSTOM.x, 0.001);
	seg_offset = INSTANCE_CUSTOM.y;
}

void fragment() {
	float along = (local_pos.x + 0.5) * seg_len + seg_offset;
	float across = abs(local_pos.z) * 2.0;
	float centre = 1.0 - smoothstep(0.0, 1.0, across);
	float pulse = smoothstep(0.85, 1.0, fract((along - TIME * pulse_speed) / 18.0));
	vec3 c = COLOR.rgb * street_color;
	ALBEDO = c * (0.35 + 0.65 * centre) * glow + c * pulse * 2.5;
}
