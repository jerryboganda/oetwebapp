import React, { useEffect, useRef } from "react";
import { Col, Card, CardBody, CardHeader } from "reactstrap";
import Sortable from "sortablejs";

const gridItems = Array.from({ length: 18 }, (_, i) => `Grid-${i + 1}`);

const DraggableGrid = () => {
  const gridListRef = useRef(null);

  useEffect(() => {
    if (gridListRef.current) {
      new Sortable(gridListRef.current, {
        swap: true,
        swapClass: "highlight",
        animation: 150,
      });
    }
  }, []);

  return (
    <Col xxl={6}>
      <Card className="equal-card">
        <CardHeader>
          <h5>Draggable Grid</h5>
        </CardHeader>
        <CardBody>
          <ul className="grid-box-list" ref={gridListRef}>
            {gridItems.map((item, index) => (
              <li key={index}>
                <div className="grid-box">
                  <h6>{item}</h6>
                </div>
              </li>
            ))}
          </ul>
        </CardBody>
      </Card>
    </Col>
  );
};

export default DraggableGrid;
