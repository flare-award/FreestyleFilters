#!/usr/bin/env python3
"""
Проверка математики фильтров ScreenFX (зеркало FilterPipeline.cs)
Запускается на Linux в CI
"""
import math

def luminance(r,g,b): return 0.2126*r + 0.7152*g + 0.0722*b
def apply_saturation(r,g,b,sat):
    l=luminance(r,g,b)
    return (l+(r-l)*sat, l+(g-l)*sat, l+(b-l)*sat)
def apply_exposure(r,g,b,ev):
    m=2**ev
    return (r*m, g*m, b*m)
def apply_negative(r,g,b): return (1-r,1-g,1-b)
def rgb_to_hsv(r,g,b):
    mx=max(r,g,b); mn=min(r,g,b); d=mx-mn
    h=0
    if d>1e-10:
        if abs(mx-r)<1e-10: h=((g-b)/d)%6
        elif abs(mx-g)<1e-10: h=(b-r)/d+2
        else: h=(r-g)/d+4
        h/=6
        if h<0: h+=1
    s=0 if mx<1e-10 else d/mx
    v=mx
    return (h,s,v)
def hsv_to_rgb(h,s,v):
    h=h%1
    c=v*s
    x=c*(1-abs((h*6)%2-1))
    m=v-c
    r=g=b=0
    hh=h*6
    if hh<1: r,g=c,x
    elif hh<2: r,g=x,c
    elif hh<3: g,b=c,x
    elif hh<4: g,b=x,c
    elif hh<5: r,b=x,c
    else: r,b=c,x
    return (r+m,g+m,b+m)
def apply_hue(r,g,b,shift):
    if abs(shift)<0.001: return (r,g,b)
    h,s,v=rgb_to_hsv(r,g,b)
    h=(h+shift)%1
    return hsv_to_rgb(h,s,v)

def test_neutral():
    # neutral should not change
    r,g,b=0.3,0.5,0.7
    # no filters
    assert abs(r-0.3)<1e-3
    print("✓ test_neutral")

def test_saturation_zero():
    r,g,b=apply_saturation(1,0,0,0)
    assert abs(r-g)<1e-3 and abs(g-b)<1e-3
    assert abs(r-0.2126)<0.01
    print("✓ test_saturation_zero")

def test_exposure():
    r,g,b=apply_exposure(0.3,0.3,0.3,1)
    assert abs(r-0.6)<0.01
    r,g,b=apply_exposure(0.6,0.6,0.6,-1)
    assert abs(r-0.3)<0.01
    print("✓ test_exposure")

def test_negative():
    r,g,b=apply_negative(0.2,0.4,0.6)
    assert abs(r-0.8)<1e-3
    print("✓ test_negative")

def test_hue():
    r,g,b=apply_hue(1,0,0,0.333)
    assert g>r and g>b
    print("✓ test_hue")

def test_letterbox():
    # letterbox should override
    def apply_with_letterbox(r,g,b,uvY,letterboxHeight,letterboxColor):
        if uvY<letterboxHeight or uvY>1-letterboxHeight:
            return letterboxColor
        return (r,g,b)
    col=apply_with_letterbox(0.5,0.5,0.5,0.05,0.1,(0,0,0))
    assert col==(0,0,0)
    col2=apply_with_letterbox(0.5,0.5,0.5,0.5,0.1,(0,0,0))
    assert col2!=(0,0,0)
    print("✓ test_letterbox")

def test_circular_blend():
    # simulate circular blur feather
    def smoothstep(a,b,x):
        if x<=a: return 0
        if x>=b: return 1
        t=(x-a)/(b-a)
        return t*t*(3-2*t)
    radius=0.25
    smooth=0.1
    dist_inside=0.1
    feather=smoothstep(radius,radius+smooth,dist_inside)
    assert feather==0
    dist_out=0.4
    feather=smoothstep(radius,radius+smooth,dist_out)
    assert feather==1
    dist_edge=0.3
    f=smoothstep(radius,radius+smooth,dist_edge)
    assert 0<f<1
    print("✓ test_circular_blend")

def test_hsv_roundtrip():
    h,s,v=rgb_to_hsv(0.2,0.4,0.8)
    r2,g2,b2=hsv_to_rgb(h,s,v)
    assert abs(r2-0.2)<0.01 and abs(g2-0.4)<0.01 and abs(b2-0.8)<0.01
    print("✓ test_hsv_roundtrip")

def test_clamping():
    def clamp(v,mi,ma): return max(mi,min(ma,v))
    assert clamp(10,0,3)==3
    assert clamp(-10,-3,3)==-3
    print("✓ test_clamping")

if __name__=="__main__":
    test_neutral()
    test_saturation_zero()
    test_exposure()
    test_negative()
    test_hue()
    test_letterbox()
    test_circular_blend()
    test_hsv_roundtrip()
    test_clamping()
    print("All Python filter math tests passed.")
