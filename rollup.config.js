var path = require("path");
var alias = require("rollup-plugin-alias");

module.exports = {
  dest: './out/bundle.js',
  // PIXI will be loaded directly from a script tag 
  external: ["PIXI"],
  // Rollup understands better es2015 modules so
  // let's load the es2015 distribution of fable-powerpack
  plugins: [alias({
    "fable-powerpack": path.resolve("node_modules/fable-powerpack/es2015")
  })]
}