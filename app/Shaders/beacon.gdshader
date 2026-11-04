// Places (PLACE-09): light columns. Beacons stand for folders beyond the current place ("hints of what follows");
// the same shader draws unknown-listing fog columns. Additive, fading with height. COLOR.a = strength.
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

varying vec3 local_pos;
varying float seed;

void vertex() {
	local_pos = VERTEX;
	seed = float(INSTANCE_ID) * 1.618;
}

void fragment() {
	float h = local_pos.y + 0.5;
	// clamp: |dot| can exceed 1 by rounding, and pow() of a negative base is NaN (glow spreads NaN into blobs).
	float rim = pow(clamp(1.0 - abs(dot(NORMAL, VIEW)), 0.0, 1.0), 1.5);
	float fade = (1.0 - h) * (1.0 - h);
	float pulse = 0.8 + 0.2 * sin(TIME * 1.3 + seed);
	ALBEDO = COLOR.rgb * COLOR.a * (0.5 + 0.5 * rim) * fade * pulse * 0.32;
}
