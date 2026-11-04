// Places (PLACE-06): the shimmering membrane inside a gate, marking the threshold you walk through. Additive and
// unshaded, so it never darkens the view through the gate. COLOR = tint (alpha = strength); INSTANCE_CUSTOM.w
// style as gate.gdshader (2 = sealed: a dense lattice that reads as "closed").
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

varying vec3 local_pos;
varying float inst_style;

void vertex() {
	local_pos = VERTEX;
	inst_style = INSTANCE_CUSTOM.w;
}

bool has_bit(float bits, float bit) { return mod(floor(bits / bit), 2.0) >= 1.0; }

void fragment() {
	vec2 uv = local_pos.xy + 0.5;
	float edge = smoothstep(0.0, 0.18, uv.x) * smoothstep(0.0, 0.18, 1.0 - uv.x) * smoothstep(0.0, 0.08, uv.y) * smoothstep(0.0, 0.12, 1.0 - uv.y);
	float scan = 0.5 + 0.5 * sin((uv.y * 22.0 - TIME * 1.6) * 3.14159);
	float veil = 0.18 + 0.22 * scan * scan;
	float lower = 1.0 - uv.y * 0.55;
	float a = COLOR.a * edge * veil * lower;
	if (has_bit(inst_style, 2.0)) {
		vec2 g = abs(fract(uv * vec2(9.0, 12.0)) - 0.5);
		a = COLOR.a * edge * (0.12 + 0.6 * (1.0 - smoothstep(0.03, 0.08, min(g.x, g.y))));
	}
	if (has_bit(inst_style, 1.0)) { a *= 0.6 + 0.4 * sin(TIME * 3.0); }
	ALBEDO = COLOR.rgb * a;
}
