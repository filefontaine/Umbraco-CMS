//this controller simply tells the dialogs service to open a mediaPicker window
//with a specified callback, this callback will receive an object with a selection on it
angular.module('umbraco')
.controller("Umbraco.PrevalueEditors.TreePickerController",
	
	function($scope, dialogService, entityResource, $log, iconHelper, miniEditorHelper){
		$scope.renderModel = [];
		$scope.ids = [];

		$scope.allowRemove = true;
		$scope.allowEdit = true;
		$scope.sortable = false;

	    var config = {
	        multiPicker: false,
	        entityType: "Document",
	        type: "content",
            treeAlias: "content",
            idType: "int"
        };

        //combine the config with any values returned from the server
        if ($scope.model.config) {
            angular.extend(config, $scope.model.config);
        }
		
		if($scope.model.value){
			$scope.ids = $scope.model.value.split(',');
			entityResource.getByIds($scope.ids, config.entityType).then(function (data) {
			    _.each(data, function (item, i) {
					entityResource.getUrl(item.id, "Document").then(function(data){
						item.path = data;
						item.icon = iconHelper.convertFromLegacyIcon(item.icon);		
						$scope.renderModel.push({name: item.name, path: item.path, id: item.id, icon: item.icon, udi: item.udi});
					}); 
			    });
			});
		}

		$scope.openContentPicker = function() {
            $scope.treePickerOverlay = config;		
            $scope.treePickerOverlay.section = config.type;
			$scope.treePickerOverlay.view = "treePicker";
            $scope.treePickerOverlay.show = true;

			$scope.treePickerOverlay.submit = function(model) {

				if(config.multiPicker) {
					populate(model.selection);
				} else {
					populate(model.selection[0]);
				}

				$scope.treePickerOverlay.show = false;
				$scope.treePickerOverlay = null;
			};

			$scope.treePickerOverlay.close = function(oldModel) {
				$scope.treePickerOverlay.show = false;
				$scope.treePickerOverlay = null;
			};

		}
		

		
	
		$scope.remove =function(index){
			$scope.renderModel.splice(index, 1);
			$scope.ids.splice(index, 1);
			$scope.model.value = trim($scope.ids.join(), ",");
		};

		$scope.clear = function() {
		    $scope.model.value = "";
		    $scope.renderModel = [];
		    $scope.ids = [];
		};
		
        $scope.add = function (item) {

            var itemId = config.idType === "udi" ? item.udi : item.id;

            if ($scope.ids.indexOf(itemId) < 0){
				item.icon = iconHelper.convertFromLegacyIcon(item.icon);
				entityResource.getUrl(item.id, "Document").then(function(data){
					item.path = data;		
					$scope.ids.push(itemId);
					$scope.renderModel.push({name: item.name, path: item.path, id: item.id, icon: item.icon, udi: item.udi});
					$scope.model.value = trim($scope.ids.join(), ",");
				});                 
			}	
		};


	    var unsubscribe = $scope.$on("formSubmitting", function (ev, args) {
			$scope.model.value = trim($scope.ids.join(), ",");
	    });

	    //when the scope is destroyed we need to unsubscribe
	    $scope.$on('$destroy', function () {
	        unsubscribe();
	    });

		function trim(str, chr) {
			var rgxtrim = (!chr) ? new RegExp('^\\s+|\\s+$', 'g') : new RegExp('^'+chr+'+|'+chr+'+$', 'g');
			return str.replace(rgxtrim, '');
		}

		function populate(data){
			if(angular.isArray(data)){
			    _.each(data, function (item, i) {
					$scope.add(item);
				});
			}else{
				$scope.clear();
				$scope.add(data);
			}
		}
});
