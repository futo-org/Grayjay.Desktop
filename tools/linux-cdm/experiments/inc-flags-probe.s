.text
.globl _start
_start:
 mov $100000,%ecx
 xor %r8d,%r8d
loop_inc:
 stc
 inc %r8d
 jz fail
 jnc fail
 dec %ecx
 jnz loop_inc
 cmp $100000,%r8d
 jne fail

 movabs $0xaabbccddffffffff,%r8
 stc
 inc %r8d
 jnz fail
 jnc fail
 test %r8,%r8
 jnz fail

 movabs $0x112233445566fffe,%r8
 stc
 inc %r8w
 jz fail
 jnc fail
 inc %r8w
 jnz fail
 jnc fail
 cmpw $0,%r8w
 jne fail
 movabs $0x1122334455660000,%r9
 cmp %r9,%r8
 jne fail

 mov $12,%r8d
 stc
 inc %r8d
 jnc fail
 cmp $13,%r8d
 jne fail

 xor %edi,%edi
 jmp finish
fail:
 mov $1,%edi
finish:
 mov $60,%eax
 syscall
