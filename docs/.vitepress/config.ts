import { defineConfig } from 'vitepress';

export default defineConfig({
  title: 'SourceGen',
  description: 'A collection of C# Source Generators for compile-time code generation',
  base: '/SourceGen/',
  head: [['link', { rel: 'icon', href: '/SourceGen/icon.png' }]],
  themeConfig: {
    logo: '/icon.png',
    nav: [
      { text: 'Guide', link: '/Ioc/01_Overview' },
      { text: 'GitHub', link: 'https://github.com/AndyElessar/SourceGen' }
    ],
    sidebar: [
      {
        text: 'Guide',
        items: [
          { text: 'Overview', link: '/Ioc/01_Overview' },
          { text: 'Basic Usage', link: '/Ioc/02_Basic' },
          { text: 'Default Settings', link: '/Ioc/03_Defaults' },
          {
            text: 'Field, Property & Method Injection',
            link: '/Ioc/04_Field_Property_Method_Injection'
          },
          { text: 'Keyed Services', link: '/Ioc/05_Keyed' },
          { text: 'Decorators', link: '/Ioc/06_Decorator' },
          { text: 'Tags', link: '/Ioc/07_Tags' },
          { text: 'Factory & Instance', link: '/Ioc/08_Factory_Instance' },
          { text: 'Open Generic Types', link: '/Ioc/09_OpenGeneric' },
          { text: 'Wrapper Types', link: '/Ioc/10_Wrapper' },
          { text: 'CLI Tool', link: '/Ioc/11_CliTool' },
          { text: 'Container Generation', link: '/Ioc/12_Container' },
          { text: 'MSBuild Configuration', link: '/Ioc/13_MSBuild_Configuration' },
          { text: 'Best Practices', link: '/Ioc/14_Best_Practices' }
        ]
      }
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/AndyElessar/SourceGen' }
    ],
    search: {
      provider: 'local'
    },
    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2026 AndyElessar'
    }
  }
});
