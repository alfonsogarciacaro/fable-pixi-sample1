 module.exports = {
    entry: './out/index.js',
    output: './outbundle.js',
    externals: [{
        PIXI: "var PIXI"
    }]
 }