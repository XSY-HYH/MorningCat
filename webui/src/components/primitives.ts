import { tv } from 'tailwind-variants';

export const title = tv({
  base: 'tracking-tight inline font-semibold',
  variants: {
    color: {
      violet: 'from-[#60A5FA] to-[#3B82F6]',
      yellow: 'from-[#FF705B] to-[#FFB457]',
      blue: 'from-[#5EA2EF] to-[#0072F5]',
      cyan: 'from-[#00b7fa] to-[#01cfea]',
      green: 'from-[#6FEE8D] to-[#17c964]',
      pink: 'from-[#5EA2EF] to-[#0072F5]',
      foreground: 'from-[#FFFFFF] to-[#4B4B4B]',
    },
    size: {
      xxs: 'text-medium lg:text-medium',
      xs: 'text-xl lg:text-xl',
      sm: 'text-3xl lg:text-4xl',
      md: 'text-[2.3rem] lg:text-5xl leading-9',
      lg: 'text-4xl lg:text-6xl',
    },
    fullWidth: {
      true: 'w-full block',
    },
    shadow: {
      true: 'drop-shadow-md',
    },
  },
  defaultVariants: {
    size: 'md',
  },
  compoundVariants: [
    {
      color: [
        'violet',
        'yellow',
        'blue',
        'cyan',
        'green',
        'pink',
        'foreground',
      ],
      class: 'bg-clip-text text-transparent bg-gradient-to-b',
    },
  ],
});

export const subtitle = tv({
  base: 'w-full md:w-1/2 my-2 text-lg lg:text-xl text-default-600 block max-w-full',
  variants: {
    fullWidth: {
      true: '!w-full',
    },
  },
  defaultVariants: {
    fullWidth: true,
  },
});
