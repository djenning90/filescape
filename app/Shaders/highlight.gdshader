// The selection/hover outline: additive glowing edges with a faint face tint. box_size is the node's world-space size.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;

uniform vec4 color : source_color = vec4(1.0, 0.95, 0.6, 1.0);
uniform vec3 box_size = vec3(1.0);
uniform float edge = 0.06;
uniform float face_alpha = 0.07;
uniform float pulse = 1.0;

varying vec3 local_pos;
varying vec3 local_normal;

void vertex() {
	local_pos = VERTEX;
	local_normal = NORMAL;
}

void fragment() {
	vec3 s = max(box_size, vec3(0.001));
	vec3 p = (local_pos + 0.5) * s;
	vec3 d = min(p, s - p);
	vec3 an = abs(local_normal);
	if (an.x > 0.5) { d.x = 1e5; } else if (an.y > 0.5) { d.y = 1e5; } else { d.z = 1e5; }
	float m = min(min(d.x, d.y), d.z);
	float e = 1.0 - smoothstep(edge * 0.5, edge, m);
	float beat = mix(1.0, 0.75 + 0.25 * sin(TIME * 5.0), pulse);
	ALBEDO = color.rgb * beat;
	ALPHA = max(e, face_alpha) * color.a;
}
