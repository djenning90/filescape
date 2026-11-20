// The overview's folded systems (PLANET-91, AD-77): a soft camera-facing haze with a faint rim, the size of the system's
// ring, over a world whose children are not drawn at this distance. It says "more inside: come closer or select it".
// One MultiMesh for every folded system; per instance COLOR (the world's tint, alpha = how folded it is now, faded
// by the app as systems open and fold). The instance's scale is the haze's radius (the quad is 2 x 2).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

void vertex() {
	float s = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * s, INV_VIEW_MATRIX[1] * s, INV_VIEW_MATRIX[2] * s, MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	float r = length(UV - vec2(0.5)) * 2.0;
	// A soft glow falling off to the edge, and a thin rim at the ring's radius.
	float glow = (1.0 - smoothstep(0.0, 1.0, r)) * 0.55;
	float rim = (1.0 - smoothstep(0.0, 0.05, abs(r - 0.9))) * 0.8;
	ALBEDO = COLOR.rgb;
	ALPHA = clamp(COLOR.a * (glow + rim), 0.0, 1.0);
}
