// Liquid Glass edge refraction for a rounded rectangle (WPF ShaderEffect, ps_2_0 so it also
// works in WPF's software renderer: RDP, weak GPUs, RenderTargetBitmap).
sampler2D input : register(s0);
float4 shape : register(c0); // x,y = size in px, z = corner radius, w = refraction band width
float4 prm   : register(c1); // x = strength in px, y = chromatic spread, z = rim brightening

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 size = shape.xy;
    float2 c = uv * size - size * 0.5;
    float2 q = abs(c) - (size * 0.5 - shape.z);
    float2 m = max(q, 0);
    float outside = length(m);
    float d = outside + min(max(q.x, q.y), 0) - shape.z; // signed distance, < 0 inside
    float t = saturate(1 + d / shape.w);
    t = t * t;
    float2 axis = (q.x > q.y) ? float2(1, 0) : float2(0, 1);
    float2 n = (outside > 0.001) ? m / outside : axis;
    n *= sign(c);
    float2 off = -n * t * prm.x / size;
    float4 g = tex2D(input, uv + off);
    g.r = tex2D(input, uv + off * (1 + prm.y)).r;
    g.b = tex2D(input, uv + off * (1 - prm.y)).b;
    g.rgb += t * prm.z * g.a;
    g *= saturate(0.5 - d); // outside the rounded shape nothing is drawn (anti-aliased edge)
    return g;
}
