// A vertical light beam above the selection (FSN spotlight / District holographic marker).
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 color : source_color = vec4(1.0, 0.9, 0.55, 0.5);

varying float height01;

void vertex() {
	height01 = VERTEX.y + 0.5;
}

void fragment() {
	float fade = pow(1.0 - height01, 1.6);
	ALBEDO = color.rgb;
	ALPHA = color.a * fade * (0.8 + 0.2 * sin(TIME * 3.0 + height01 * 12.0));
}
