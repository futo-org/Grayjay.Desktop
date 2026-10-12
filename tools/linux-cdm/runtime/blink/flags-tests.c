#include <stdio.h>
#include <stdlib.h>
#include <assert.h>
#include "blink/alu.h"
#include "blink/flags.h"
#include "flags-under-test.inc"
#define GOLDEN(M, TYPE, X, Y, OP, COMMUT)                             \
  ({                                                                    \
    TYPE Res;                                                           \
    u32 OldFlags = (M)->flags & ~(OF | SF | ZF | AF | CF);              \
    u64 NewFlags;                                                       \
    asm(OP "\t%3,%0\n\t"                                                \
        "pushfq\n\t"                                                    \
        "pop\t%1"                                                       \
        : "=&r" (Res), "=r" (NewFlags)                                  \
        : COMMUT "0" ((TYPE)(X)), "g" ((TYPE)(Y))                       \
        : "cc");                                                        \
    (M)->flags = OldFlags | ((u32)NewFlags & (OF | SF | ZF | AF | CF)); \
    Res;                                                                \
  })
#define GOLDEN_CF(M, TYPE, X, Y, OP, COMMUT)                          \
  ({                                                                    \
    TYPE Res;                                                           \
    u32 OldFlags = (M)->flags & ~(OF | SF | ZF | AF);                   \
    u64 NewFlags;                                                       \
    asm("btr\t%5,%2\n\t"                                                \
        OP "\t%4,%0\n\t"                                                \
        "pushfq\n\t"                                                    \
        "pop\t%1"                                                       \
        : "=&r" (Res), "=r" (NewFlags), "+&r" (OldFlags)                \
        : COMMUT "0" ((TYPE)(X)), "g" ((TYPE)(Y)),                      \
          "i" (FLAGS_CF)                                                \
        : "cc");                                                        \
    (M)->flags = OldFlags | ((u32)NewFlags & (OF | SF | ZF | AF | CF)); \
    Res;                                                                \
  })
i64 Golden0_8(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u8,x,y,"add",""); }
i64 Golden0_16(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u16,x,y,"add",""); }
i64 Golden0_32(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u32,x,y,"add",""); }
i64 Golden0_64(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u64,x,y,"add",""); }
i64 Golden1_8(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u8,x,y,"or",""); }
i64 Golden1_16(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u16,x,y,"or",""); }
i64 Golden1_32(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u32,x,y,"or",""); }
i64 Golden1_64(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u64,x,y,"or",""); }
i64 Golden2_8(struct Machine*m,u64 x,u64 y) { return GOLDEN_CF(m,u8,x,y,"adc",""); }
i64 Golden2_16(struct Machine*m,u64 x,u64 y) { return GOLDEN_CF(m,u16,x,y,"adc",""); }
i64 Golden2_32(struct Machine*m,u64 x,u64 y) { return GOLDEN_CF(m,u32,x,y,"adc",""); }
i64 Golden2_64(struct Machine*m,u64 x,u64 y) { return GOLDEN_CF(m,u64,x,y,"adc",""); }
i64 Golden3_8(struct Machine*m,u64 x,u64 y) { return GOLDEN_CF(m,u8,x,y,"sbb",""); }
i64 Golden3_16(struct Machine*m,u64 x,u64 y) { return GOLDEN_CF(m,u16,x,y,"sbb",""); }
i64 Golden3_32(struct Machine*m,u64 x,u64 y) { return GOLDEN_CF(m,u32,x,y,"sbb",""); }
i64 Golden3_64(struct Machine*m,u64 x,u64 y) { return GOLDEN_CF(m,u64,x,y,"sbb",""); }
i64 Golden4_8(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u8,x,y,"and",""); }
i64 Golden4_16(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u16,x,y,"and",""); }
i64 Golden4_32(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u32,x,y,"and",""); }
i64 Golden4_64(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u64,x,y,"and",""); }
i64 Golden5_8(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u8,x,y,"sub",""); }
i64 Golden5_16(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u16,x,y,"sub",""); }
i64 Golden5_32(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u32,x,y,"sub",""); }
i64 Golden5_64(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u64,x,y,"sub",""); }
i64 Golden6_8(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u8,x,y,"xor",""); }
i64 Golden6_16(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u16,x,y,"xor",""); }
i64 Golden6_32(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u32,x,y,"xor",""); }
i64 Golden6_64(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u64,x,y,"xor",""); }
i64 Golden7_8(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u8,x,y,"sub",""); }
i64 Golden7_16(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u16,x,y,"sub",""); }
i64 Golden7_32(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u32,x,y,"sub",""); }
i64 Golden7_64(struct Machine*m,u64 x,u64 y) { return GOLDEN(m,u64,x,y,"sub",""); }
const aluop_f golden[8][4]={{Golden0_8,Golden0_16,Golden0_32,Golden0_64},{Golden1_8,Golden1_16,Golden1_32,Golden1_64},{Golden2_8,Golden2_16,Golden2_32,Golden2_64},{Golden3_8,Golden3_16,Golden3_32,Golden3_64},{Golden4_8,Golden4_16,Golden4_32,Golden4_64},{Golden5_8,Golden5_16,Golden5_32,Golden5_64},{Golden6_8,Golden6_16,Golden6_32,Golden6_64},{Golden7_8,Golden7_16,Golden7_32,Golden7_64}};
static u64 random64(void) {static u64 state=0x243f6a8885a308d3;state^=state<<13;state^=state>>7;state^=state<<17;return state;}
int main(void) {
 struct Machine *actual=calloc(1,sizeof(*actual)),*expected=calloc(1,sizeof(*expected));assert(actual&&expected);
 for(int op=0;op<8;++op)for(int width=0;width<4;++width)for(int n=0;n<20000;++n) {
  u64 x=random64(),y=random64();if(n<128){x=n%2 ? ~0ULL : 0;y=n/2;}
  actual->flags=expected->flags=(u32)random64();
  u64 want=golden[op][width](expected,x,y),got=kAluFast[op][width](actual,x,y);
  if(want!=got || actual->flags!=expected->flags){fprintf(stderr,"Mismatch op=%d width=%d x=%llx y=%llx result=%llx/%llx flags=%x/%x\n",op,width,(unsigned long long)x,(unsigned long long)y,(unsigned long long)got,(unsigned long long)want,actual->flags,expected->flags);return 1;}
 }
 puts("PASS 640000 arithmetic cases: results, carry, overflow, sign, zero, auxiliary and preserved parity flags");
 return 0;
}
