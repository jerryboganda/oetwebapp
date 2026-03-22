import React, { useMemo, useRef, useState } from "react";
import { Card, CardBody, CardHeader, Col } from "reactstrap";

const userData = [
  { id: 1, name: "Olive Yew", born: "1986", img: "7.png" },
  { id: 2, name: "Olive Yew", born: "1957", img: "7.png" },
  { id: 3, name: "Allie Grater", born: "1860", img: "1.png" },
  { id: 4, name: "Rita Book", born: "1976", img: "16.png" },
  { id: 5, name: "Rose Bush", born: "1960", img: "4.png" },
];

const ListTable: React.FC = () => {
  const usersRef = useRef<HTMLDivElement>(null);
  const [searchTerm, setSearchTerm] = useState("");
  const [sortByName, setSortByName] = useState(false);

  const filteredData = useMemo(() => {
    let list = [...userData];

    if (searchTerm.trim()) {
      list = list.filter(
        (user) =>
          user.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
          user.born.includes(searchTerm)
      );
    }

    if (sortByName) {
      list.sort((a, b) => a.name.localeCompare(b.name));
    }

    return list;
  }, [searchTerm, sortByName]);

  return (
    <Col lg={4}>
      <Card>
        <CardHeader>
          <h5>List table</h5>
        </CardHeader>
        <CardBody>
          <div id="users" ref={usersRef}>
            <div className="row">
              <div className="col-12 col-sm">
                <div className="mb-3">
                  <input
                    type="search"
                    className="form-control search b-r-22"
                    placeholder="Search..."
                    aria-label="Search"
                    value={searchTerm}
                    onChange={(e) => setSearchTerm(e.target.value)}
                  />
                </div>
              </div>
              <div className="col-12 col-sm-auto">
                <div className="mb-3">
                  <button
                    className="sort btn btn-sm btn-secondary b-r-22"
                    onClick={() => setSortByName((prev) => !prev)}
                  >
                    Sort by name
                  </button>
                </div>
              </div>
            </div>
            <div className="list list-tables">
              {filteredData.length > 0 ? (
                filteredData.map(({ id, name, born, img }) => (
                  <div
                    key={id}
                    data-id={id}
                    className="d-flex align-items-center justify-content-between mt-2"
                  >
                    <input
                      className="form-check-input mt-0"
                      type="checkbox"
                      name="item"
                    />
                    <div className="link name ps-3 flex-grow-1 pe-2">
                      <p className="mb-0">{name}</p>
                      <h6 className="fw-bold">{born}</h6>
                    </div>
                    <div className="h-25 w-25 d-flex-center b-r-50 overflow-hidden text-bg-secondary">
                      <img
                        src={`/images/avatar/${img}`}
                        alt="avatar"
                        className="img-fluid"
                      />
                    </div>
                  </div>
                ))
              ) : (
                <p className="text-muted">No results found</p>
              )}
            </div>
          </div>
        </CardBody>
      </Card>
    </Col>
  );
};

export default ListTable;
