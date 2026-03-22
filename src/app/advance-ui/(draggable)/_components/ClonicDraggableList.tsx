import React, { useEffect, useRef } from "react";
import { Col, Card, CardBody, CardHeader } from "reactstrap";
import { DotsThreeVertical } from "@phosphor-icons/react";
import Sortable from "sortablejs";

const clonicMenuItemsLeft = [
  { label: "A", title: "Clonic Menu List 1" },
  { label: "B", title: "Clonic Menu List 2" },
  { label: "C", title: "Clonic Menu List 3" },
  { label: "D", title: "Clonic Menu List 4" },
  { label: "E", title: "Clonic Menu List 5" },
];

const clonicMenuItemsRight = [
  { label: "A", title: "Clonic Menu List 6" },
  { label: "B", title: "Clonic Menu List 7" },
  { label: "C", title: "Clonic Menu List 8" },
  { label: "D", title: "Clonic Menu List 9" },
  { label: "E", title: "Clonic Menu List 10" },
];

const ClonicDraggableList = () => {
  const clonicMenuLeft = useRef(null);
  const clonicMenuRight = useRef(null);
  useEffect(() => {
    if (clonicMenuLeft.current) {
      new Sortable(clonicMenuLeft.current, {
        group: { name: "shared1", pull: "clone", put: false },
        animation: 150,
        sort: false,
      });
    }
    if (clonicMenuRight.current) {
      new Sortable(clonicMenuRight.current, {
        group: { name: "shared1", pull: "clone" },
        animation: 150,
      });
    }
  }, []);

  return (
    <Col xxl={6}>
      <Card className="equal-card">
        <CardHeader>
          <h5>Draggable Clonic List</h5>
        </CardHeader>
        <CardBody>
          <div className="row">
            <Col xs={6} sm={6} md={6} lg={6} className="box-layout-draggable">
              <ul className="clonic-menu-list" ref={clonicMenuLeft}>
                {clonicMenuItemsLeft.map((item, index) => (
                  <li key={index}>
                    <div className="clonic-menu-item" draggable="false">
                      <span className="text-light-primary h-40 w-40 d-flex-center b-r-50 clonic-menu-img">
                        {item.label}
                      </span>
                      <div className="clonic-menu-content">
                        <h6 className="mb-0">{item.title}</h6>
                      </div>
                      <span>
                        <DotsThreeVertical size={18} />
                      </span>
                    </div>
                  </li>
                ))}
              </ul>
            </Col>
            <Col xs={6} sm={6} md={6} lg={6} className="box-layout-draggable">
              <ul className="clonic-menu-list" ref={clonicMenuRight}>
                {clonicMenuItemsRight.map((item, index) => (
                  <li key={index}>
                    <div className="clonic-menu-item" draggable="false">
                      <span className="text-light-primary h-40 w-40 d-flex-center b-r-50 clonic-menu-img">
                        {item.label}
                      </span>
                      <div className="clonic-menu-content">
                        <h6 className="mb-0">{item.title}</h6>
                      </div>
                      <span>
                        <DotsThreeVertical size={18} />
                      </span>
                    </div>
                  </li>
                ))}
              </ul>
            </Col>
          </div>
        </CardBody>
      </Card>
    </Col>
  );
};

export default ClonicDraggableList;
